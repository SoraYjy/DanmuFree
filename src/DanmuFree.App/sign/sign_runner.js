// 接收 md5（argv[2]），跑同目录 sign.js 的 getSign，把 X-Bogus 写 stdout（stderr 留给警告/错误）。
// sign.js 是 saermart 的 webmssdk(1.0.0.53) 逆向 + jsdom 补环境产物，需本地 node_modules 有 jsdom。
const fs = require('fs');
const path = require('path');
const md5 = process.argv[2];
if (!md5) { console.error('usage: node sign_runner.js <md5>'); process.exit(2); }
const src = fs.readFileSync(path.join(__dirname, 'sign.js'), 'utf8');
// 把 sign.js 包进 IIFE 执行（const 不泄露），返回 getSign 结果（saermart execjs 同款思路）。
const wrapped = "(function(){\n" + src + "\n;return getSign({'X-MS-STUB':" + JSON.stringify(md5) + "});\n})()";
const r = eval(wrapped);
process.stdout.write((r && r['X-Bogus']) || '');
