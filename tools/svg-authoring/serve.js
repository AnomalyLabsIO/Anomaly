'use strict';

const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const root = __dirname;
const portArgument = process.argv.indexOf('--port');
const requestedPort = portArgument >= 0 ? Number(process.argv[portArgument + 1]) : Number(process.env.VECTOR_STUDIO_PORT || 8765);
const port = Number.isInteger(requestedPort) && requestedPort >= 0 && requestedPort <= 65535 ? requestedPort : 8765;

const routes = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/index.html', ['index.html', 'text/html; charset=utf-8']],
  ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/editor-core.js', ['editor-core.js', 'text/javascript; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']]
]);

const securityHeaders = {
  'Cache-Control': 'no-store',
  'Content-Security-Policy': "default-src 'self' data: blob:; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'",
  'Cross-Origin-Resource-Policy': 'same-origin',
  'Referrer-Policy': 'no-referrer',
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY'
};

const server = http.createServer((request, response) => {
  let pathname;
  try {
    pathname = new URL(request.url, 'http://127.0.0.1').pathname;
  } catch {
    response.writeHead(400, securityHeaders);
    response.end('Bad request');
    return;
  }

  if (pathname === '/favicon.ico') {
    response.writeHead(204, securityHeaders);
    response.end();
    return;
  }

  const route = routes.get(pathname);
  if (!route || (request.method !== 'GET' && request.method !== 'HEAD')) {
    response.writeHead(route ? 405 : 404, { ...securityHeaders, 'Content-Type': 'text/plain; charset=utf-8' });
    response.end(route ? 'Method not allowed' : 'Not found');
    return;
  }

  const [filename, contentType] = route;
  fs.readFile(path.join(root, filename), (error, contents) => {
    if (error) {
      response.writeHead(500, { ...securityHeaders, 'Content-Type': 'text/plain; charset=utf-8' });
      response.end('The local studio file could not be read.');
      return;
    }
    response.writeHead(200, { ...securityHeaders, 'Content-Type': contentType, 'Content-Length': contents.length });
    if (request.method === 'HEAD') response.end();
    else response.end(contents);
  });
});

server.on('error', (error) => {
  console.error(`Vector Studio could not start on 127.0.0.1:${port}: ${error.message}`);
  process.exitCode = 1;
});

server.listen(port, '127.0.0.1', () => {
  const address = server.address();
  console.log(`Anomaly Vector Studio is ready at http://127.0.0.1:${address.port}/`);
  console.log('Press Ctrl+C to stop the local server.');
});

function stop() {
  server.close(() => process.exit(0));
}

process.on('SIGINT', stop);
process.on('SIGTERM', stop);
