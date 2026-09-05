#!/usr/bin/env node
/*
 * generate-platform-phrases.mjs — one-time (and re-runnable) synthesizer for the platform
 * TTS phrase library.
 *
 * Reads the canonical catalog at ContactConnection.Web/src/data/platformPhrases.json and, for
 * every phrase in every voice, synthesizes the phrase text (English phrases in the `en` voices,
 * Spanish in the `es` voices) via ElevenLabs' documented streaming WebSocket protocol — the same
 * protocol ContactConnection.Infrastructure/Tts/ElevenLabsTtsStreamProvider.cs speaks — then
 * transcodes the PCM to OGG Vorbis 8 kHz mono (identical ffmpeg params to
 * AudioFilesEndpoints.TranscodeToOggAsync) and writes it to:
 *
 *     freeswitch/sounds/_platform/{voiceKey}/{phraseKey}.ogg
 *
 * That path is mounted into the FreeSWITCH container at
 * /usr/share/freeswitch/sounds/contactconnection/_platform/... and is committed to the repo, so
 * the library ships to every environment with no runtime ElevenLabs dependency. Flow JSON refers
 * to a clip as "__platform:{voiceKey}/{phraseKey}"; TelephonyAudioResolver expands that.
 *
 * Requires: Node 22+ (global WebSocket), ffmpeg on PATH (or FFMPEG_PATH env var),
 *           ELEVENLABS_API_KEY env var.
 *
 * Usage:
 *   node scripts/generate-platform-phrases.mjs --dry-run          # plan + character/credit estimate, no API calls
 *   node scripts/generate-platform-phrases.mjs                     # generate everything missing
 *   node scripts/generate-platform-phrases.mjs --force             # re-generate everything, overwriting
 *   node scripts/generate-platform-phrases.mjs --voice=will        # only this voice
 *   node scripts/generate-platform-phrases.mjs --phrase=hold_please_hold   # only this phrase (all its voices)
 *   node scripts/generate-platform-phrases.mjs --voice=annie --phrase=callback_offer --force
 */

import { readFileSync, existsSync, mkdirSync, writeFileSync, rmSync, statSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as sleep } from 'node:timers/promises';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..');
const CATALOG_PATH = join(REPO_ROOT, 'ContactConnection.Web', 'src', 'data', 'platformPhrases.json');
const OUT_ROOT = join(REPO_ROOT, 'freeswitch', 'sounds', '_platform');
const FFMPEG = process.env.FFMPEG_PATH || 'ffmpeg';
const MODEL_ID = 'eleven_flash_v2_5';
const PCM_RATE = 16000; // ElevenLabs has no pcm_8000; nearest-up, then ffmpeg downsamples to 8k
const VOICE_SETTINGS = { stability: 0.5, similarity_boost: 0.75, style: 0.0, use_speaker_boost: true, speed: 1.0 };
const INTER_REQUEST_DELAY_MS = 350;

// ---- args -------------------------------------------------------------------
const args = process.argv.slice(2);
const has = (f) => args.includes(f);
const val = (k) => { const a = args.find((x) => x.startsWith(`--${k}=`)); return a ? a.split('=')[1] : null; };
const DRY_RUN = has('--dry-run');
const FORCE = has('--force');
const ONLY_VOICE = val('voice');
const ONLY_PHRASE = val('phrase');

// ---- catalog --------------------------------------------------------------
const catalog = JSON.parse(readFileSync(CATALOG_PATH, 'utf8'));
let voices = catalog.voices;
let phrases = catalog.phrases;
if (ONLY_VOICE) voices = voices.filter((v) => v.key === ONLY_VOICE);
if (ONLY_PHRASE) phrases = phrases.filter((p) => p.key === ONLY_PHRASE);

if (voices.length === 0) { console.error(`No voice matched --voice=${ONLY_VOICE}`); process.exit(1); }
if (phrases.length === 0) { console.error(`No phrase matched --phrase=${ONLY_PHRASE}`); process.exit(1); }

// Build the work list: one item per (voice, phrase) where the phrase has text for the voice's language.
const jobs = [];
for (const v of voices) {
  for (const p of phrases) {
    const text = p[v.lang];
    if (!text) continue;
    const outPath = join(OUT_ROOT, v.key, `${p.key}.ogg`);
    jobs.push({ voice: v, phrase: p, text, outPath });
  }
}

const totalChars = jobs.reduce((n, j) => n + j.text.length, 0);
console.log(`Catalog: ${catalog.voices.length} voices, ${catalog.phrases.length} phrases`);
console.log(`Selected: ${voices.length} voice(s), ${phrases.length} phrase(s) -> ${jobs.length} clips, ${totalChars} characters total`);
console.log(`Estimated ElevenLabs cost: ~${Math.round(totalChars * 0.5)} credits (flash v2.5 @ 0.5/char) / ~${totalChars} at standard 1/char`);
console.log(`Output root: ${OUT_ROOT}`);

if (DRY_RUN) {
  console.log('\n--dry-run: no API calls. Planned clips:');
  for (const j of jobs) {
    const state = existsSync(j.outPath) ? (FORCE ? 'OVERWRITE' : 'skip (exists)') : 'create';
    console.log(`  [${state.padEnd(14)}] ${j.voice.key}/${j.phrase.key}  (${j.text.length} ch, ${j.voice.lang})`);
  }
  process.exit(0);
}

const API_KEY = process.env.ELEVENLABS_API_KEY;
if (!API_KEY) { console.error('ELEVENLABS_API_KEY env var is not set.'); process.exit(1); }

// ---- ElevenLabs streaming synth -> raw PCM (Int16 LE) ----------------------
function synthesize(voiceId, text) {
  return new Promise((res, rej) => {
    const url =
      `wss://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voiceId)}/stream-input` +
      `?model_id=${encodeURIComponent(MODEL_ID)}&output_format=pcm_${PCM_RATE}`;
    const ws = new WebSocket(url, { headers: { 'xi-api-key': API_KEY } });
    const chunks = [];
    let settled = false;
    const done = (fn, arg) => { if (settled) return; settled = true; try { ws.close(); } catch {} fn(arg); };
    const timer = setTimeout(() => done(rej, new Error('timeout after 60s')), 60_000);

    ws.addEventListener('open', () => {
      ws.send(JSON.stringify({ text: ' ', voice_settings: VOICE_SETTINGS }));
      ws.send(JSON.stringify({ text: text.trimEnd() + ' ', flush: true }));
      ws.send(JSON.stringify({ text: '' }));
    });
    ws.addEventListener('message', (ev) => {
      let msg;
      try { msg = JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8')); }
      catch { return; }
      if (msg.audio) {
        const buf = Buffer.from(msg.audio, 'base64');
        if (buf.length) chunks.push(buf);
      } else if (msg.error) {
        clearTimeout(timer);
        done(rej, new Error(`ElevenLabs error: ${JSON.stringify(msg)}`));
        return;
      }
      if (msg.isFinal) { clearTimeout(timer); done(res, Buffer.concat(chunks)); }
    });
    ws.addEventListener('error', (ev) => { clearTimeout(timer); done(rej, new Error(`WebSocket error: ${ev.message || ev}`)); });
    ws.addEventListener('close', () => { clearTimeout(timer); done(res, Buffer.concat(chunks)); });
  });
}

// ---- 44-byte-header WAV (16-bit mono PCM) ---------------------------------
function wavFromPcm(pcm, sampleRate) {
  const channels = 1, bitsPerSample = 16;
  const byteRate = sampleRate * channels * bitsPerSample / 8;
  const blockAlign = channels * bitsPerSample / 8;
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + pcm.length, 4); h.write('WAVE', 8);
  h.write('fmt ', 12); h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(channels, 22);
  h.writeUInt32LE(sampleRate, 24); h.writeUInt32LE(byteRate, 28); h.writeUInt16LE(blockAlign, 32);
  h.writeUInt16LE(bitsPerSample, 34);
  h.write('data', 36); h.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([h, pcm]);
}

function transcodeToOgg(wavPath, oggPath) {
  return new Promise((res, rej) => {
    const p = spawn(FFMPEG, ['-y', '-i', wavPath, '-vn', '-ar', '8000', '-ac', '1', '-c:a', 'libvorbis', '-q:a', '3', oggPath], { stdio: ['ignore', 'ignore', 'pipe'] });
    let stderr = '';
    p.stderr.on('data', (d) => { stderr += d; });
    p.on('error', (e) => rej(new Error(`ffmpeg could not start (${FFMPEG}): ${e.message}`)));
    p.on('close', (code) => {
      if (code === 0 && existsSync(oggPath)) res();
      else rej(new Error(`ffmpeg exited ${code}. stderr: ${stderr.trim().split('\n').slice(-3).join(' | ')}`));
    });
  });
}

// ---- run ----------------------------------------------------------------
let created = 0, skipped = 0, failed = 0;
const failures = [];

for (let i = 0; i < jobs.length; i++) {
  const j = jobs[i];
  const tag = `(${i + 1}/${jobs.length}) ${j.voice.key}/${j.phrase.key}`;
  if (existsSync(j.outPath) && !FORCE) { skipped++; console.log(`  skip   ${tag} (exists)`); continue; }

  mkdirSync(dirname(j.outPath), { recursive: true });
  const tmpWav = j.outPath.replace(/\.ogg$/, '.tmp.wav');
  try {
    const pcm = await synthesize(j.voice.id, j.text);
    if (!pcm || pcm.length === 0) throw new Error('no audio returned');
    writeFileSync(tmpWav, wavFromPcm(pcm, PCM_RATE));
    await transcodeToOgg(tmpWav, j.outPath);
    const kb = (statSync(j.outPath).size / 1024).toFixed(1);
    created++;
    console.log(`  ok     ${tag}  ${j.text.length}ch -> ${kb} KB`);
  } catch (err) {
    failed++;
    failures.push({ job: `${j.voice.key}/${j.phrase.key}`, error: err.message });
    console.error(`  FAIL   ${tag}: ${err.message}`);
  } finally {
    if (existsSync(tmpWav)) rmSync(tmpWav);
  }
  if (i < jobs.length - 1) await sleep(INTER_REQUEST_DELAY_MS);
}

console.log(`\nDone. created=${created} skipped=${skipped} failed=${failed}`);
if (failures.length) {
  console.log('Failures:');
  for (const f of failures) console.log(`  ${f.job}: ${f.error}`);
  process.exit(1);
}
