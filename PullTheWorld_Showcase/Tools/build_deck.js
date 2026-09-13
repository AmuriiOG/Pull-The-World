// Pull The World - pitch deck: PPTX (one full-bleed image per slide, 16:9) and a PDF of the same
// slides. The slide images themselves are composed by compose.ps1 (slides). Run from this folder:
//   node build_deck.js
const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");
const pptxgen = require("pptxgenjs");   // npm install in this folder

const FFMPEG = process.env.FFMPEG || "ffmpeg";
const ROOT = path.resolve(__dirname, "..");
const PRES = path.join(ROOT, "Presentation");
const SLIDES = path.join(PRES, "Slides");
const slides = fs.readdirSync(SLIDES).filter(f => /^slide_\d\d.*\.png$/i.test(f)).sort();
if (slides.length === 0) throw new Error("no slides in " + SLIDES);

const NOTES = {
  1: "Pull the World - a calm physics puzzle where you never move the character: you rotate the whole island and gravity carries a glowing orb home.",
  2: "The idea in one sentence. The fantasy: holding a small painted world in your hands and tipping it.",
  3: "The core mechanic: one gesture. Drag anywhere to rotate the island; the orb rolls with gravity; the portal is the goal.",
  4: "Situations built from a few readable pieces: spikes, gems, water, boulders that break crates, spring pads, urchins.",
  5: "Progression: levels stand in one continuous world. Finish an island and the camera pushes forward to the next, which was already visible in the sky.",
  6: "Visual identity: pastel dawn, painted parallax layers, glass orb, glowing portal, grass, vines, flowers, particles.",
  7: "Why it feels good: understood in a second, physics you can feel, a world that never cuts, one idea per level.",
  8: "Pull the World.",
};

(async () => {
  const pptx = new pptxgen();
  pptx.layout = "LAYOUT_16x9";           // 10 x 5.625 in
  pptx.title = "Pull the World";
  pptx.author = "Obscure Games";
  slides.forEach((f, i) => {
    const s = pptx.addSlide();
    s.background = { path: path.join(SLIDES, f) };
    if (NOTES[i + 1]) s.addNotes(NOTES[i + 1]);
  });
  const pptxPath = path.join(PRES, "PullTheWorld_Pitch.pptx");
  await pptx.writeFile({ fileName: pptxPath });
  console.log("wrote", pptxPath);

  // PDF: each slide as a JPEG page, 16:9 at 960 x 540 pt, hand-written PDF objects.
  const jpgs = slides.map(f => {
    const out = path.join(ROOT, "RawCaptures", "_work", f.replace(/\.png$/i, ".jpg"));
    execFileSync(FFMPEG, ["-hide_banner", "-loglevel", "error", "-y", "-i", path.join(SLIDES, f), "-q:v", "2", out]);
    return fs.readFileSync(out);
  });
  const objs = [];                       // object bodies as Buffers, 1-based in the PDF
  const add = b => { objs.push(Buffer.isBuffer(b) ? b : Buffer.from(b, "latin1")); return objs.length; };
  const catalogId = add("<< /Type /Catalog /Pages 2 0 R >>");
  const pagesId = add("PLACEHOLDER");
  const pageIds = [];
  jpgs.forEach((jpg, i) => {
    const imgId = add(Buffer.concat([Buffer.from(`<< /Type /XObject /Subtype /Image /Width 1920 /Height 1080 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpg.length} >>\nstream\n`, "latin1"), jpg, Buffer.from("\nendstream", "latin1")]));
    const content = "q 960 0 0 540 0 0 cm /Im0 Do Q";
    const contentId = add(`<< /Length ${content.length} >>\nstream\n${content}\nendstream`);
    const pageId = add(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 960 540] /Resources << /XObject << /Im0 ${imgId} 0 R >> >> /Contents ${contentId} 0 R >>`);
    pageIds.push(pageId);
  });
  objs[pagesId - 1] = Buffer.from(`<< /Type /Pages /Kids [${pageIds.map(id => id + " 0 R").join(" ")}] /Count ${pageIds.length} >>`, "latin1");
  const parts = [Buffer.from("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n", "latin1")];
  const offsets = [];
  let pos = parts[0].length;
  objs.forEach((b, i) => { offsets.push(pos); const head = Buffer.from(`${i + 1} 0 obj\n`, "latin1"); const tail = Buffer.from("\nendobj\n", "latin1"); parts.push(head, b, tail); pos += head.length + b.length + tail.length; });
  const xref = pos;
  let x = `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n`;
  offsets.forEach(o => { x += String(o).padStart(10, "0") + " 00000 n \n"; });
  x += `trailer\n<< /Size ${objs.length + 1} /Root ${catalogId} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  parts.push(Buffer.from(x, "latin1"));
  const pdfPath = path.join(PRES, "PullTheWorld_Pitch.pdf");
  fs.writeFileSync(pdfPath, Buffer.concat(parts));
  console.log("wrote", pdfPath, slides.length, "pages");
})().catch(e => { console.error(e); process.exit(1); });
