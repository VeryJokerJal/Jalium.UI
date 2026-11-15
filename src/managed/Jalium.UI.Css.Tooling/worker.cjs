"use strict";

const path = require("node:path");
const { pathToFileURL, fileURLToPath } = require("node:url");
const sass = require("sass");
const less = require("less");
const prettier = require("prettier/standalone");
const postcss = require("prettier/plugins/postcss");

function diagnostic(error, fallbackPath) {
  const span = error.span;
  const loc = error.loc?.start;
  return {
    message: String(error.sassMessage || error.message || error),
    filePath: span?.url?.protocol === "file:" ? fileURLToPath(span.url) : error.filename || fallbackPath,
    line: span ? span.start.line + 1 : error.line || loc?.line || 1,
    column: span ? span.start.column + 1 : error.column != null ? error.column + 1 : loc?.column || 1,
  };
}

async function run(request) {
  const sourcePath = path.resolve(request.path);
  if (request.operation === "format") {
    const formatted = await prettier.format(request.text, {
      parser: request.language,
      plugins: [postcss],
      filepath: sourcePath,
      ...(request.rangeStart == null ? {} : { rangeStart: request.rangeStart, rangeEnd: request.rangeEnd }),
    });
    return { text: formatted, imports: [], sourceMap: null, diagnostics: [] };
  }
  if (request.language === "css")
    return { text: request.text, imports: [], sourceMap: null, diagnostics: [] };
  if (request.language === "scss") {
    const result = sass.compileString(request.text, {
      url: pathToFileURL(sourcePath),
      loadPaths: [path.dirname(sourcePath)],
      sourceMap: true,
      style: "expanded",
    });
    return {
      text: result.css,
      imports: result.loadedUrls.filter(url => url.protocol === "file:").map(fileURLToPath),
      sourceMap: JSON.stringify(result.sourceMap),
      diagnostics: [],
    };
  }
  if (request.language === "less") {
    const result = await less.render(request.text, {
      filename: sourcePath,
      paths: [path.dirname(sourcePath)],
      sourceMap: { sourceMapOutputFilename: sourcePath + ".css.map", outputSourceFiles: true },
    });
    return {
      text: result.css.replace(/\/\*# sourceMappingURL=[^*]*\*\/\s*$/, ""),
      imports: result.imports,
      sourceMap: result.map || null,
      diagnostics: [],
    };
  }
  throw new Error("Unknown CSS language: " + request.language);
}

let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", chunk => { input += chunk; });
process.stdin.on("end", async () => {
  try {
    const request = JSON.parse(input);
    process.stdout.write(JSON.stringify(await run(request)));
  } catch (error) {
    const requestPath = (() => { try { return JSON.parse(input).path; } catch { return null; } })();
    process.stdout.write(JSON.stringify({ text: null, imports: [], sourceMap: null,
      diagnostics: [diagnostic(error, requestPath)] }));
  }
});
