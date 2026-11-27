// WhatsApp Blast - sends each phone number its own picture through WhatsApp Web.
//
//   node send.js          -> send to everyone in the list, using WhatsApp Web in your Chrome
//   node send.js --check  -> only check the list (numbers + picture files), sends nothing
//   node send.js --new-excel -> make a contacts.xlsx to fill in
//   node send.js --to-excel  -> turn contacts.csv into contacts.xlsx
//
// The list is contacts.xlsx (or contacts.csv if there is no .xlsx) in this folder.
// Columns: phone | image | caption   (caption is optional)

const fs = require('fs');
const path = require('path');
const XLSX = require('xlsx');

// ===== Settings =====
const GAP_SECONDS = 30; // wait between each chat
const USE_MY_CHROME = true; // true = the Chrome you already use; false = a separate Chrome window
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'; // separate window only
// ====================

const DIR = __dirname;
// node send.js --list contacts-test.csv  -> use another list; its log is sent-log-<list name>.csv
const LIST_ARG = (() => { const i = process.argv.indexOf('--list'); return i > 0 ? process.argv[i + 1] : null; })();
const LOG_FILE = path.join(DIR, LIST_ARG ? `sent-log-${path.parse(LIST_ARG).name}.csv` : 'sent-log.csv');
const CHECK_ONLY = process.argv.includes('--check');
// Chrome writes this file while "Allow remote debugging" is switched on.
const PORT_FILE = path.join(process.env.LOCALAPPDATA || '', 'Google', 'Chrome', 'User Data', 'DevToolsActivePort');

const MY_CHROME_HELP = `
Could not connect to your Chrome. To switch the connection on:
  1. Open your Chrome (the one logged in to WhatsApp).
  2. Type  chrome://inspect/#remote-debugging  in the address bar.
  3. Tick "Allow remote debugging for this browser instance".
  4. Run 3-SEND.bat again, and click "Allow" if Chrome asks.`;

function myChromeAddress() {
  if (!fs.existsSync(PORT_FILE)) return null;
  const [port, address] = fs.readFileSync(PORT_FILE, 'utf8').split('\n').map(s => s.trim());
  return /^\d+$/.test(port) && address ? `ws://127.0.0.1:${port}${address}` : null;
}

function findListFile() {
  if (LIST_ARG) {
    const p = path.isAbsolute(LIST_ARG) ? LIST_ARG : path.join(DIR, LIST_ARG);
    return fs.existsSync(p) ? p : null;
  }
  for (const name of ['contacts.xlsx', 'contacts.csv']) {
    const p = path.join(DIR, name);
    if (fs.existsSync(p)) return p;
  }
  return null;
}

// 012-345 6789 -> 60123456789. Excel drops the leading 0, so 123456789 -> 60123456789 too.
function cleanPhone(raw) {
  let d = String(raw ?? '').replace(/\D/g, '');
  if (d.startsWith('0')) d = '6' + d;
  else if (d.startsWith('1') && d.length >= 9 && d.length <= 10) d = '60' + d;
  return d;
}

function findImage(raw) {
  const name = String(raw ?? '').trim();
  if (!name) return null;
  const candidates = [
    path.isAbsolute(name) ? name : path.join(DIR, name),
    path.join(DIR, 'pics', name),
  ];
  return candidates.find(p => fs.existsSync(p) && fs.statSync(p).isFile()) || null;
}

// A CSV is read as UTF-8 text, so captions with accents or Chinese are not garbled.
function openBook(file, raw = false) {
  if (/\.csv$/i.test(file)) {
    return XLSX.read(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''), { type: 'string', raw });
  }
  return XLSX.readFile(file, { raw: false });
}

function readList(file) {
  const wb = openBook(file);
  const rows = XLSX.utils.sheet_to_json(wb.Sheets[wb.SheetNames[0]], { defval: '' });
  return rows.map((row, i) => {
    const get = key => {
      const k = Object.keys(row).find(c => c.trim().toLowerCase() === key);
      return k ? row[k] : '';
    };
    const imageRaw = String(get('image')).trim();
    return {
      row: i + 2, // spreadsheet row number (row 1 is the header)
      phoneRaw: String(get('phone')).trim(),
      phone: cleanPhone(get('phone')),
      imageRaw,
      image: findImage(imageRaw),
      caption: String(get('caption') ?? '').trim(),
    };
  }).filter(r => r.phoneRaw || r.imageRaw);
}

function problemsOf(r) {
  const p = [];
  if (r.phone.length < 10 || r.phone.length > 15) p.push(`bad phone "${r.phoneRaw}"`);
  if (!r.imageRaw) p.push('no image given');
  else if (!r.image) p.push(`image not found "${r.imageRaw}"`);
  return p;
}

const logSafe = text => String(text).replace(/[\r\n,]+/g, ' ');

// "Already sent" means this picture to this number, so one number can get two different pictures.
const sentKey = (phone, picture) => `${phone}|${logSafe(picture).toLowerCase()}`;

function alreadySent() {
  if (!fs.existsSync(LOG_FILE)) return new Set();
  return new Set(fs.readFileSync(LOG_FILE, 'utf8').split(/\r?\n/)
    .map(l => l.split(',').slice(-3)) // phone, status, detail (read from the end of the line)
    .filter(c => c[1] === 'SENT')
    .map(c => sentKey(c[0], c[2])));
}

// The guest's name in the report is the picture's file name without its extension.
const guestName = r => path.basename(r.imageRaw).replace(/\.(png|jpe?g|gif|webp)$/i, '');

function log(r, status, detail = '') {
  if (!fs.existsSync(LOG_FILE)) fs.writeFileSync(LOG_FILE, 'time,name,phone,status,detail\n');
  const time = new Date().toLocaleString('en-GB').replace(',', '');
  const cells = [time, guestName(r), r.phone || r.phoneRaw, status, detail];
  fs.appendFileSync(LOG_FILE, cells.map(logSafe).join(',') + '\n');
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

// ===== Excel list (used by the app's "Contact list" buttons) =====
const XLSX_LIST = path.join(DIR, 'contacts.xlsx');
const CSV_LIST = path.join(DIR, 'contacts.csv');

// Phone numbers are stored as text, so Excel cannot turn them into 6.01E+10.
function writeExcelList(file, rows) {
  const ws = XLSX.utils.aoa_to_sheet([['phone', 'image', 'caption'], ...rows.map(r => r.map(v => String(v ?? '')))]);
  for (let i = 1; i <= rows.length; i++) {
    const cell = ws[XLSX.utils.encode_cell({ r: i, c: 0 })];
    if (cell) { cell.t = 's'; cell.z = '@'; }
  }
  ws['!cols'] = [{ wch: 18 }, { wch: 40 }, { wch: 45 }];
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, 'contacts');
  XLSX.writeFile(wb, file);
}

// node send.js --new-excel : make contacts.xlsx with two dummy rows to fill in
function newExcel() {
  if (fs.existsSync(XLSX_LIST)) { console.log('contacts.xlsx already exists.'); process.exitCode = 2; return; }
  writeExcelList(XLSX_LIST, [
    ['60000000001', 'sample1.png', 'Dear, Guest One'],
    ['60000000002', 'sample2.png', 'Dear, Guest Two'],
  ]);
  console.log('Made contacts.xlsx with 2 example rows.');
}

// node send.js --to-excel : turn contacts.csv into contacts.xlsx, check nothing changed,
// then keep the CSV as contacts-before-excel.csv
function toExcel() {
  if (!fs.existsSync(CSV_LIST)) { console.log('contacts.csv not found.'); process.exitCode = 1; return; }
  if (fs.existsSync(XLSX_LIST)) { console.log('contacts.xlsx already exists.'); process.exitCode = 2; return; }
  const wb = openBook(CSV_LIST, true); // raw: keep numbers exactly as typed
  const [head = [], ...body] = XLSX.utils.sheet_to_json(wb.Sheets[wb.SheetNames[0]], { header: 1, defval: '', raw: false });
  const col = name => head.findIndex(h => String(h).trim().toLowerCase() === name);
  const idx = ['phone', 'image', 'caption'].map(col);
  const rows = body.map(r => idx.map(i => (i < 0 ? '' : String(r[i] ?? '').trim())))
    .filter(r => r[0] || r[1]);
  writeExcelList(XLSX_LIST, rows);

  const key = r => [r.phone, r.imageRaw, r.caption].join('|');
  const a = readList(CSV_LIST).map(key), b = readList(XLSX_LIST).map(key);
  if (a.length !== b.length || a.some((k, i) => k !== b[i])) {
    fs.unlinkSync(XLSX_LIST);
    console.log('Could not convert the list exactly, so nothing was changed.');
    process.exitCode = 1;
    return;
  }
  let backup = path.join(DIR, 'contacts-before-excel.csv');
  if (fs.existsSync(backup)) backup = path.join(DIR, `contacts-before-excel-${Date.now()}.csv`);
  fs.renameSync(CSV_LIST, backup);
  console.log(`Made contacts.xlsx with ${b.length} rows. The old list is kept as ${path.basename(backup)}.`);
}

async function main() {
  if (process.argv.includes('--new-excel')) return newExcel();
  if (process.argv.includes('--to-excel')) return toExcel();

  const file = findListFile();
  if (!file) {
    console.log('No contacts.xlsx or contacts.csv found in this folder.');
    return;
  }
  const list = readList(file);
  console.log(`List: ${path.basename(file)} - ${list.length} rows\n`);

  let bad = 0;
  for (const r of list) {
    const p = problemsOf(r);
    if (p.length) { bad++; console.log(`  Row ${r.row}: ${p.join(', ')}`); }
  }
  console.log(bad ? `\n${bad} row(s) have problems and will be skipped.` : 'All rows look OK.');

  if (CHECK_ONLY) {
    const ok = list.length - bad;
    const mins = Math.ceil((Math.max(ok - 1, 0) * GAP_SECONDS) / 60);
    console.log(`${ok} row(s) ready to send. With a ${GAP_SECONDS}s gap that takes about ${mins} minute(s).`);
    return;
  }

  const { Client, LocalAuth, MessageMedia } = require('whatsapp-web.js');
  const qrcode = require('qrcode-terminal');

  const address = USE_MY_CHROME ? myChromeAddress() : null;
  if (USE_MY_CHROME && !address) {
    console.log(MY_CHROME_HELP);
    return;
  }

  const client = new Client(USE_MY_CHROME ? {
    puppeteer: {
      browserWSEndpoint: address,
      defaultViewport: null, // keep the tab at its normal size
      // Connect only to blank and WhatsApp tabs, not to your other tabs.
      targetFilter: t => ['', 'about:blank'].includes(t.url()) || t.url().startsWith('https://web.whatsapp.com'),
    },
    userAgent: false,         // keep your Chrome's own identity
    takeoverOnConflict: true, // if WhatsApp is also open in another tab, use this one
    authTimeoutMs: 60000,     // how long to wait for WhatsApp to load
  } : {
    authStrategy: new LocalAuth({ dataPath: path.join(DIR, '.login') }),
    puppeteer: {
      headless: false,
      executablePath: fs.existsSync(CHROME_PATH) ? CHROME_PATH : undefined,
    },
  });

  if (USE_MY_CHROME) {
    // The library's own destroy() would close your whole Chrome. Close only the tab we opened.
    client.destroy = async () => {
      await client.pupPage?.close().catch(() => {});
      await client.pupBrowser?.disconnect();
    };
  }

  client.on('qr', qr => {
    console.log(USE_MY_CHROME
      ? '\nThe new tab is not logged in to WhatsApp. If it opened in the wrong Chrome profile,\n' +
        'close this window, click your logged-in Chrome window, then run again. Or scan this QR:'
      : '\nScan this QR with WhatsApp on your phone (Linked devices > Link a device):');
    qrcode.generate(qr, { small: true });
  });
  client.on('auth_failure', m => console.log('Login failed:', m));

  let waitingHint;
  client.on('ready', async () => {
    clearTimeout(waitingHint);
    console.log('\nLogged in. Starting...\n');
    const done = alreadySent();
    let sent = 0, skipped = 0, failed = 0, first = true;

    for (const r of list) {
      const tag = `Row ${r.row} (${r.phone})`;
      const p = problemsOf(r);
      if (p.length) { log(r, 'SKIPPED', p.join('; ')); skipped++; continue; }
      const key = sentKey(r.phone, path.basename(r.image));
      if (done.has(key)) { console.log(`${tag}: ${path.basename(r.image)} already sent before - skipped`); skipped++; continue; }

      if (!first) {
        console.log(`   waiting ${GAP_SECONDS}s...`);
        await sleep(GAP_SECONDS * 1000);
      }
      first = false;

      try {
        const id = await client.getNumberId(r.phone);
        if (!id) {
          console.log(`${tag}: not on WhatsApp`);
          log(r, 'NOT_ON_WHATSAPP');
          failed++;
          continue;
        }
        const media = MessageMedia.fromFilePath(r.image);
        await client.sendMessage(id._serialized, media, r.caption ? { caption: r.caption } : {});
        console.log(`${tag}: SENT ${path.basename(r.image)}`);
        log(r, 'SENT', path.basename(r.image));
        done.add(key);
        sent++;
      } catch (e) {
        console.log(`${tag}: FAILED - ${e.message}`);
        log(r, 'FAILED', e.message);
        failed++;
      }
    }

    console.log(`\nFinished. Sent ${sent}, skipped ${skipped}, failed ${failed}. Details in ${path.basename(LOG_FILE)}`);
    await sleep(5000); // let the last picture finish uploading
    await client.destroy();
    process.exit(0);
  });

  console.log(USE_MY_CHROME
    ? '\nOpening WhatsApp in a new tab of your Chrome... click "Allow" if Chrome asks.'
    : '\nOpening WhatsApp Web...');
  waitingHint = setTimeout(() => console.log('\nStill waiting for WhatsApp. Look at the WhatsApp tab: ' +
    'if it says it is open in another window, click "Use here".'), 45000);
  client.initialize().catch(e => {
    console.log(`\nCould not start: ${e.message || e}`);
    if (USE_MY_CHROME) console.log(client.pupBrowser
      ? 'WhatsApp did not become ready in the new tab. Check that it opened in the Chrome profile\n' +
        'that is logged in to WhatsApp (click that Chrome window first), then run again.'
      : MY_CHROME_HELP);
    process.exit(1);
  });
}

main().catch(e => { console.error(e); process.exit(1); });
