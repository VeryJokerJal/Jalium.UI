#!/usr/bin/env python3
"""Generate a real H.264/AAC/multi-track fixture and exercise local + HTTP URI paths."""

from __future__ import annotations

import argparse
import functools
import http.server
import os
import pathlib
import re
import subprocess
import threading
from typing import Optional, Tuple


class RangeRequestHandler(http.server.SimpleHTTPRequestHandler):
    """SimpleHTTPRequestHandler with the byte ranges GStreamer seeks with."""

    protocol_version = "HTTP/1.1"
    _byte_range: Optional[Tuple[int, int]] = None
    _advertise_ranges = False

    def send_head(self):  # type: ignore[no-untyped-def]
        self._byte_range = None
        self._advertise_ranges = False
        range_header = self.headers.get("Range")
        if not range_header:
            self._advertise_ranges = os.path.isfile(self.translate_path(self.path))
            return super().send_head()

        path = self.translate_path(self.path)
        if os.path.isdir(path):
            return super().send_head()
        try:
            source = open(path, "rb")
        except OSError:
            self.send_error(http.HTTPStatus.NOT_FOUND, "File not found")
            return None

        size = os.fstat(source.fileno()).st_size
        match = re.fullmatch(r"bytes=(\d*)-(\d*)", range_header.strip())
        if not match or (not match.group(1) and not match.group(2)):
            source.close()
            self.send_error(http.HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
            return None

        if match.group(1):
            start = int(match.group(1))
            end = int(match.group(2)) if match.group(2) else size - 1
        else:
            suffix = int(match.group(2))
            start = max(0, size - suffix)
            end = size - 1
        end = min(end, size - 1)
        if start >= size or start > end:
            source.close()
            self.send_response(http.HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
            self.send_header("Content-Range", f"bytes */{size}")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return None

        self._byte_range = (start, end)
        self.send_response(http.HTTPStatus.PARTIAL_CONTENT)
        self.send_header("Content-type", self.guess_type(path))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.send_header("Content-Length", str(end - start + 1))
        self.send_header("Last-Modified", self.date_time_string(os.fstat(source.fileno()).st_mtime))
        self.end_headers()
        return source

    def end_headers(self) -> None:
        if self._advertise_ranges:
            self.send_header("Accept-Ranges", "bytes")
            self._advertise_ranges = False
        super().end_headers()

    def copyfile(self, source, outputfile) -> None:  # type: ignore[no-untyped-def]
        if self._byte_range is None:
            return super().copyfile(source, outputfile)
        start, end = self._byte_range
        source.seek(start)
        remaining = end - start + 1
        while remaining > 0:
            block = source.read(min(64 * 1024, remaining))
            if not block:
                break
            outputfile.write(block)
            remaining -= len(block)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", required=True)
    parser.add_argument(
        "--managed-smoke",
        help="Optional managed smoke DLL to exercise with the same file and URI.",
    )
    parser.add_argument("--ffmpeg")
    parser.add_argument("--subtitle")
    parser.add_argument("--workdir")
    parser.add_argument(
        "--fixture",
        help="Use an existing fixture instead of generating one (manual repros).",
    )
    args = parser.parse_args()

    if args.fixture:
        fixture = pathlib.Path(args.fixture).resolve()
        if not fixture.is_file():
            parser.error(f"fixture does not exist: {fixture}")
        workdir = fixture.parent
    else:
        if not args.ffmpeg or not args.subtitle or not args.workdir:
            parser.error(
                "--ffmpeg, --subtitle and --workdir are required when "
                "--fixture is not provided"
            )
        workdir = pathlib.Path(args.workdir).resolve()
        workdir.mkdir(parents=True, exist_ok=True)
        fixture = workdir / "jalium-h264-multitrack.mkv"

        subprocess.run(
            [
                args.ffmpeg,
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "testsrc2=size=96x64:rate=10:duration=2",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:sample_rate=48000:duration=2",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=880:sample_rate=48000:duration=2",
                "-i",
                str(pathlib.Path(args.subtitle).resolve()),
                "-map",
                "0:v:0",
                "-map",
                "1:a:0",
                "-map",
                "2:a:0",
                "-map",
                "3:s:0",
                "-c:v",
                "libx264",
                "-preset",
                "ultrafast",
                "-tune",
                "zerolatency",
                "-pix_fmt",
                "yuv420p",
                "-g",
                "10",
                "-c:a",
                "aac",
                "-b:a",
                "64k",
                "-c:s",
                "srt",
                "-metadata:s:a:0",
                "language=eng",
                "-metadata:s:a:0",
                "title=English tone",
                "-metadata:s:a:1",
                "language=jpn",
                "-metadata:s:a:1",
                "title=Japanese tone",
                "-metadata:s:s:0",
                "language=eng",
                "-metadata:s:s:0",
                "title=English captions",
                "-cues_to_front",
                "1",
                str(fixture),
            ],
            check=True,
        )

    handler = functools.partial(RangeRequestHandler, directory=str(workdir))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        uri = (
            f"http://127.0.0.1:{server.server_address[1]}/"
            f"{fixture.name}"
        )
        completed = subprocess.run(
            [args.smoke, str(fixture), str(fixture), str(fixture), uri],
            check=False,
        )
        if completed.returncode != 0 or not args.managed_smoke:
            return completed.returncode
        for source in (str(fixture), uri):
            completed = subprocess.run(
                ["dotnet", args.managed_smoke, source],
                check=False,
            )
            if completed.returncode != 0:
                return completed.returncode
        return 0
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


if __name__ == "__main__":
    raise SystemExit(main())
