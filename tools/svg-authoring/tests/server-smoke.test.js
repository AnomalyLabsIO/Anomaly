'use strict';

const path = require('node:path');
const { spawn } = require('node:child_process');
const test = require('node:test');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');

function startServer() {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['serve.js', '--port', '0'], {
      cwd: root,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe']
    });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`Local server startup timed out. ${stderr}`));
    }, 5000);

    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.stdout.on('data', (chunk) => {
      stdout += chunk;
      const match = stdout.match(/ready at (http:\/\/127\.0\.0\.1:\d+\/)/);
      if (match) {
        clearTimeout(timer);
        resolve({ child, url: match[1] });
      }
    });
    child.once('error', (error) => {
      clearTimeout(timer);
      reject(error);
    });
    child.once('exit', (code) => {
      if (!stdout.includes('ready at')) {
        clearTimeout(timer);
        reject(new Error(`Local server exited early with code ${code}. ${stderr}`));
      }
    });
  });
}

function stopServer(child) {
  return new Promise((resolve) => {
    if (child.exitCode !== null) {
      resolve();
      return;
    }
    const timer = setTimeout(() => {
      child.kill();
      resolve();
    }, 3000);
    child.once('exit', () => {
      clearTimeout(timer);
      resolve();
    });
    child.kill('SIGTERM');
  });
}

test('loopback server serves only the authoring assets with security headers', async () => {
  const { child, url } = await startServer();
  try {
    const expected = [
      ['', 'text/html'],
      ['styles.css', 'text/css'],
      ['editor-core.js', 'text/javascript'],
      ['app.js', 'text/javascript']
    ];
    for (const [route, contentType] of expected) {
      const response = await fetch(`${url}${route}`);
      assert.equal(response.status, 200);
      assert.match(response.headers.get('content-type'), new RegExp(`^${contentType}`));
      assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
      assert.match(response.headers.get('content-security-policy'), /connect-src 'none'/);
      assert.ok((await response.arrayBuffer()).byteLength > 0);
    }

    const missing = await fetch(`${url}not-a-route`);
    assert.equal(missing.status, 404);
  } finally {
    await stopServer(child);
  }
});
