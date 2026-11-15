const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

(async () => {
  let request;
  try {
    request = JSON.parse(fs.readFileSync(0, 'utf8'));
    let text, sourceMap;
    if (request.operation === 'compile') {
      if (request.language === 'scss') {
        const result = require('sass').compileString(request.source, {
          url: pathToFileURL(path.resolve(request.filePath)), sourceMap: true,
          sourceMapIncludeSources: true, loadPaths: [path.dirname(path.resolve(request.filePath))],
          logger: { warn() {}, debug() {} }
        });
        text = result.css;
        sourceMap = JSON.stringify(result.sourceMap);
      } else if (request.language === 'less') {
        const result = await require('less').render(request.source, {
          filename: path.resolve(request.filePath), javascriptEnabled: false,
          sourceMap: { outputSourceFiles: true }
        });
        text = result.css;
        sourceMap = result.map;
      } else text = request.source;
    } else {
      text = await require('prettier').format(request.source, {
        parser: request.language, rangeStart: request.start,
        rangeEnd: request.end, endOfLine: 'lf'
      });
    }
    process.stdout.write(JSON.stringify({ success: true, text, sourceMap, diagnostics: [] }));
  } catch (error) {
    const span = error.span?.start;
    process.stdout.write(JSON.stringify({ success: false, diagnostics: [{
      filePath: request?.filePath, message: error.message,
      line: span ? span.line + 1 : error.line || 1,
      column: span ? span.column + 1 : (error.column || 0) + 1
    }] }));
  }
})();
