# Metal / D3D12 renderer parity contract

The authoritative baseline is checkpoint `91c3242d` on
`Jal/metal-apple-parity` (the Vello 0.10 working tree requested for this port).

| Capability | D3D12 | Metal implementation | Gate |
|---|---|---|---|
| Device/queue/error recovery | DXGI + D3D12 queue/fence | MTLDevice + command queue + three-frame semaphore/completion retirement | device-loss harness |
| Window/composition present | flip swap chain/DComp | CAMetalLayer + persistent scene texture | present/readback smoke |
| Primitive/path rendering | SDF + Impeller/Vello | Metal SDF batches + shared triangulation + Metal Vello 19-stage graph | pixel parity scenes |
| Text/bitmap | DirectWrite atlas/WIC | CoreText shaping/raster cache + ImageIO/Metal textures | text/image scenes |
| Effects/captures | compute/pixel pipelines | Metal capture textures + compute blur/effect pipelines | nested effect scenes |
| Ink/video | D3D12 compute/DXVA surface | Metal compute ink + CVPixelBuffer/IOSurface/MTLTexture | Ink/media smoke |
| Retained layers/damage | persistent textures/dirty rect | persistent Metal textures/scissored scene texture | retained/damage tests |
| Diagnostics | queries/readback/stats | command GPU times, stats and BGRA8 blit readback | diagnostics tests |

Run `python tools/metal_parity_check.py` on every backend-interface change.
The only allow-listed omissions are Windows HANDLE pacing and the two
D3D12-specific leaked-command-list/Vello-orphan race injectors.
