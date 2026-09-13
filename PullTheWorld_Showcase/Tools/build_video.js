// Pull The World - trailer assembly.
// Reads the frame sequences + SFX rendered by the Unity test PtwShowcase (RenderClips /
// RenderMontage), trims each shot, joins them - hard cuts by concatenation, dissolves by xfade -
// lays the game's own music under the cut, overlays the logo and a few lines of Poppins, and
// writes the portrait master plus a landscape presentation cut. Run from this folder:
//   node build_video.js
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const FFMPEG = process.env.FFMPEG || "ffmpeg";   // ffmpeg 6+ on PATH, or point FFMPEG at the exe
const ROOT = path.resolve(__dirname, "..");
const PROJECT = path.resolve(ROOT, "..");
const CLIPS = path.join(ROOT, "RawCaptures", "clips");
const WORK = path.join(ROOT, "RawCaptures", "_work", "clips");
const OUT = path.join(ROOT, "Video");
const MUSIC = path.join(PROJECT, "Captures", "music_1_Dawn.wav");   // written by "Pull The World/Render Music To WAV"
fs.mkdirSync(WORK, { recursive: true });
fs.mkdirSync(OUT, { recursive: true });

const FPS = 30;
const INK = "0x46594F", CREAM = "0xFDF1DD";
// drawtext cannot take a drive letter in fontfile, so the fonts are addressed relative to this folder
const FONT_B = "../../Assets/PullTheWorld/Art/Fonts/Poppins-Bold.ttf", FONT_S = "../../Assets/PullTheWorld/Art/Fonts/Poppins-SemiBold.ttf";
const LOGO = path.join(PROJECT, "Assets", "PullTheWorld", "Art", "UI", "Layer 9.png");

// ---------------------------------------------------------------- the edit ------------------
// in/out in seconds within the rendered shot; `xf` = transition INTO the next shot:
// { type: "fade", d } for a dissolve, { type: "cut" } for a hard cut.
const EDIT = [
  { shot: "01_hook",             in: 0.30, out: 3.55, xf: { type: "fade", d: 0.6 } },
  { shot: "02_title",            in: 0.00, out: 3.60, xf: { type: "fade", d: 0.7 } },
  { shot: "03_cinematic",        in: 0.00, out: 4.20, xf: { type: "fade", d: 0.5 } },
  { shot: "04_mechanic_travel",  in: 0.00, out: null, xf: { type: "fade", d: 0.5 } },
  { shot: "05_spikes",           in: 0.20, out: 3.30, xf: { type: "cut" } },
  { shot: "06_gem",              in: 0.20, out: 3.60, xf: { type: "cut" } },
  { shot: "07_water",            in: 0.20, out: 3.20, xf: { type: "cut" } },
  { shot: "08_rock",             in: 0.10, out: 3.20, xf: { type: "cut" } },
  { shot: "09_spring",           in: 0.10, out: 3.20, xf: { type: "cut" } },
  { shot: "10_enemies",          in: 0.10, out: 3.30, xf: { type: "cut" } },
  { shot: "12_dropin",           in: 0.20, out: 3.20, xf: { type: "cut" } },
  { shot: "11_golden",           in: 0.60, out: 3.60, xf: { type: "fade", d: 0.5 } },
  { shot: "14_parallax_travel",  in: 0.00, out: null, xf: { type: "fade", d: 0.8 } },
  { shot: "15_endcard",          in: 0.00, out: 5.00, xf: null },
];

// Text overlays: shot, start (s into the shot's trimmed span), duration, text, style.
const TEXTS = [
  { shot: "02_title", at: 1.3, d: 2.3, text: "A CALM PUZZLE ABOUT PULLING THE WORLD INTO PLACE", size: 34, y: 1780, font: FONT_S },
  { shot: "03_cinematic", at: 0.8, d: 3.0, text: "ROTATE THE WORLD.", size: 54, y: 1590, font: FONT_B },
  { shot: "03_cinematic", at: 1.3, d: 2.5, text: "GRAVITY DOES THE REST.", size: 54, y: 1665, font: FONT_B },
  { shot: "04_mechanic_travel", at: 4.6, d: 2.6, text: "ONE CONTINUOUS WORLD", size: 50, y: 1620, font: FONT_B },
  { shot: "04_mechanic_travel", at: 4.9, d: 2.3, text: "FINISH AN ISLAND. THE CAMERA TRAVELS TO THE NEXT.", size: 30, y: 1700, font: FONT_S },
  { shot: "06_gem", at: 0.3, d: 2.6, text: "GEMS TO FETCH", size: 44, y: 1640, font: FONT_B },
  { shot: "08_rock", at: 0.3, d: 2.6, text: "ROCKS THAT BREAK THINGS", size: 44, y: 1640, font: FONT_B },
  { shot: "10_enemies", at: 0.3, d: 2.6, text: "50 HANDCRAFTED LEVELS", size: 44, y: 1640, font: FONT_B },
  { shot: "11_golden", at: 0.4, d: 2.5, text: "THREE CHAPTERS, THREE SKIES", size: 44, y: 1640, font: FONT_B },
  { shot: "15_endcard", at: 1.4, d: 3.6, text: "OBSCURE GAMES", size: 34, y: 1800, font: FONT_S },
];

// Logo overlays: shot, start into the trimmed span, duration, width, centre y.
const LOGOS = [
  { shot: "02_title", at: 0.35, d: 3.25, w: 900, cy: 1560 },
  { shot: "15_endcard", at: 0.6, d: 4.4, w: 900, cy: 1560 },
];

// ---------------------------------------------------------------- helpers -------------------
function run(args, label) {
  process.stdout.write((label || args.join(" ").slice(0, 80)) + " ... ");
  try { execFileSync(FFMPEG, ["-hide_banner", "-loglevel", "error", "-y", ...args], { cwd: __dirname, stdio: ["ignore", "inherit", "inherit"] }); console.log("ok"); }
  catch (e) { console.log("FAILED"); throw e; }
}
function shotInfo(name) { return JSON.parse(fs.readFileSync(path.join(CLIPS, name, "shot.json"), "utf8")); }
function ff(x) { return x.toFixed(3); }
// drawtext needs ':' ',' '%' escaped inside the text; a straight quote becomes a typographic one
function esc(s) { return s.replace(/\\/g, "\\\\").replace(/:/g, "\\:").replace(/'/g, "\u2019").replace(/,/g, "\\,").replace(/%/g, "\\%"); }
const isCut = c => !c.xf || c.xf.type === "cut";

// 1. Trim every shot into a uniform intermediate (video + SFX) so every later join can assume
//    identical streams: 1080x1920 yuv420p 30 fps, 48 kHz stereo PCM.
const clips = [];
for (const e of EDIT) {
  const info = shotInfo(e.shot);
  const total = info.frames / FPS;
  const inS = e.in, outS = e.out == null ? total : Math.min(e.out, total);
  const frames = Math.round((outS - inS) * FPS);
  const dur = frames / FPS;
  const out = path.join(WORK, e.shot + ".mov");
  const frameDir = path.join(CLIPS, e.shot).replace(/\\/g, "/");
  run([
    "-framerate", String(FPS), "-start_number", String(Math.round(inS * FPS)), "-i", frameDir + "/f_%04d.jpg",
    "-ss", ff(inS), "-i", frameDir + "/sfx.wav",
    "-frames:v", String(frames), "-t", ff(dur),
    "-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv420p", "-r", String(FPS),
    "-c:a", "pcm_s16le", "-ar", "48000", "-ac", "2", "-shortest", out,
  ], `trim ${e.shot} (${ff(dur)} s)`);
  clips.push({ ...e, dur, file: out });
}

// 2. Segments: a run of hard cuts is concatenated into one intermediate; dissolves join segments.
//    (Faking a cut with a one-frame xfade let timestamp rounding leave the chain a frame short,
//    and the video silently stopped at the next dissolve. Concat has no such edge.)
const segments = [];
let run_ = [];
for (let i = 0; i < clips.length; i++) {
  run_.push(clips[i]);
  const last = i === clips.length - 1;
  if (!isCut(clips[i]) || last) { segments.push(run_); run_ = []; }
}
const segs = segments.map((members, si) => {
  if (members.length === 1) return { file: members[0].file, dur: members[0].dur, xf: members[0].xf, members };
  const file = path.join(WORK, `seg_${si}.mov`);
  const inputs = []; members.forEach(m => inputs.push("-i", m.file));
  const maps = members.map((_, k) => `[${k}:v][${k}:a]`).join("");
  run([...inputs, "-filter_complex", `${maps}concat=n=${members.length}:v=1:a=1[v][a]`, "-map", "[v]", "-map", "[a]",
       "-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv420p", "-r", String(FPS), "-c:a", "pcm_s16le", file],
      `concat segment ${si} (${members.map(m => m.shot).join(", ")})`);
  return { file, dur: members.reduce((s, m) => s + m.dur, 0), xf: members[members.length - 1].xf, members };
});

// 3. Timeline: segment starts given the dissolve overlaps, then each shot's absolute start.
let t = 0;
for (let i = 0; i < segs.length; i++) {
  segs[i].start = t;
  let inner = t;
  for (const m of segs[i].members) { m.start = inner; inner += m.dur; }
  t += segs[i].dur - (i < segs.length - 1 ? segs[i].xf.d : 0);
}
const TOTAL = t;
console.log(`timeline: ${clips.length} shots in ${segs.length} segments, ${ff(TOTAL)} s`);
for (const c of clips) console.log(`  ${c.shot.padEnd(22)} ${ff(c.start)} -> ${ff(c.start + c.dur)}`);

// 4. The dissolve chain over the segments.
const inputs = [];
segs.forEach(s => inputs.push("-i", s.file));
const filt = [];
let v = "[0:v]", a = "[0:a]";
for (let i = 1; i < segs.length; i++) {
  const d = segs[i - 1].xf.d;
  const vo = `[v${i}]`, ao = `[a${i}]`;
  filt.push(`${v}[${i}:v]xfade=transition=fade:duration=${ff(d)}:offset=${ff(segs[i].start)}${vo}`);
  filt.push(`${a}[${i}:a]acrossfade=d=${ff(d)}:c1=tri:c2=tri${ao}`);
  v = vo; a = ao;
}

// 5. Fade the whole thing in from the sky's blush and out at the end.
filt.push(`${v}fade=t=in:st=0:d=0.5:color=0xF6E1E4,fade=t=out:st=${ff(TOTAL - 0.8)}:d=0.8:color=0xF6E1E4[vf]`);
v = "[vf]";

// 6. Logo overlays: the PNG looped as a short movie, faded in/out on alpha, shifted to its start.
let extraInputs = 0;
for (const L of LOGOS) {
  const c = clips.find(x => x.shot === L.shot); if (!c) continue;
  const start = c.start + L.at, idx = segs.length + extraInputs;
  inputs.push("-loop", "1", "-framerate", String(FPS), "-t", ff(L.d), "-i", LOGO);
  const lw = L.w, lh = Math.round(L.w * 373 / 845);
  filt.push(`[${idx}:v]scale=${lw}:${lh}:flags=lanczos,format=rgba,fade=t=in:st=0:d=0.7:alpha=1,fade=t=out:st=${ff(L.d - 0.7)}:d=0.7:alpha=1,setpts=PTS+${ff(start)}/TB[lg${extraInputs}]`);
  filt.push(`${v}[lg${extraInputs}]overlay=x=(W-w)/2:y=${Math.round(L.cy - lh / 2)}:enable='between(t,${ff(start)},${ff(start + L.d)})':eof_action=pass[vl${extraInputs}]`);
  v = `[vl${extraInputs}]`;
  extraInputs++;
}

// 7. Text: drawtext with a fade expressed through alpha.
let ti = 0;
for (const T of TEXTS) {
  const c = clips.find(x => x.shot === T.shot); if (!c) continue;
  const s = c.start + T.at, e = s + T.d, fd = 0.45;
  const alpha = `if(lt(t,${ff(s + fd)}),(t-${ff(s)})/${fd},if(gt(t,${ff(e - fd)}),(${ff(e)}-t)/${fd},1))`;
  filt.push(`${v}drawtext=fontfile=${T.font}:text='${esc(T.text)}':fontsize=${T.size}:fontcolor=${INK}:x=(w-text_w)/2:y=${T.y}:shadowcolor=${CREAM}@0.55:shadowx=0:shadowy=2:alpha='${alpha}':enable='between(t,${ff(s)},${ff(e)})'[vt${ti}]`);
  v = `[vt${ti}]`; ti++;
}

// 8. Music bed under the SFX: the game's Dawn loop (60 s), resampled, fading in and out with the
//    cut; the mix lifted a little into a limiter (the raw mix sat at -19.8 LUFS, this lands ~-16).
inputs.push("-i", MUSIC);
const mi = segs.length + extraInputs;
filt.push(`[${mi}:a]aresample=48000,aformat=channel_layouts=stereo,volume=0.62,afade=t=in:st=0:d=1.2,afade=t=out:st=${ff(TOTAL - 3.0)}:d=3.0,atrim=0:${ff(TOTAL)}[mus]`);
filt.push(`${a}volume=0.95,apad,atrim=0:${ff(TOTAL)}[sfx]`);
filt.push(`[sfx][mus]amix=inputs=2:duration=first:dropout_transition=0:normalize=0,volume=1.5,alimiter=limit=0.95:attack=5:release=60[aout]`);

const master = path.join(OUT, "PullTheWorld_Trailer_1080x1920.mp4");
run([...inputs, "-filter_complex", filt.join(";"), "-map", v, "-map", "[aout]",
     "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-r", String(FPS), "-movflags", "+faststart",
     "-c:a", "aac", "-b:a", "192k", "-t", ff(TOTAL), master], "assemble master");

// 9. Landscape presentation cut: the portrait master centred over a blurred, darkened fill of itself.
const wide = path.join(OUT, "PullTheWorld_Trailer_1920x1080.mp4");
run(["-i", master, "-filter_complex",
     "[0:v]split=2[bg][fg];[bg]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,gblur=sigma=42,eq=brightness=-0.03:saturation=0.9[bgb];[fg]scale=-2:1080:flags=lanczos[fgs];[bgb][fgs]overlay=(W-w)/2:0[out]",
     "-map", "[out]", "-map", "0:a", "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-c:a", "copy", wide], "landscape cut");

// 10. Contact sheet of the finished master, one frame every 2 seconds, for review.
run(["-i", master, "-vf", `fps=1/2,scale=180:-1,tile=6x${Math.ceil(TOTAL / 2 / 6)}`, "-frames:v", "1", "-update", "1", path.join(ROOT, "RawCaptures", "_work", "master_sheet.png")], "master sheet");
fs.writeFileSync(path.join(ROOT, "RawCaptures", "_work", "timeline.json"), JSON.stringify({ total: TOTAL, clips: clips.map(c => ({ shot: c.shot, start: c.start, dur: c.dur })) }, null, 2));
console.log("done", master);
