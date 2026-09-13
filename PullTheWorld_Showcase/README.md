# Pull the World — Showcase Package

Everything here was rendered from the real game (Unity 6000.2, the `Game` scene) at a fixed 30 fps,
then assembled with ffmpeg. Nothing is mocked up: every frame is the playable build.

## Deliverables

| What | Where |
| --- | --- |
| Trailer, portrait master (1080 x 1920, 49.5 s, H.264 + AAC) | `Video/PullTheWorld_Trailer_1080x1920.mp4` |
| Trailer, landscape presentation cut (1920 x 1080) | `Video/PullTheWorld_Trailer_1920x1080.mp4` |
| Pitch deck, PowerPoint (8 slides, 16:9, with speaker notes) | `Presentation/PullTheWorld_Pitch.pptx` |
| Pitch deck, PDF | `Presentation/PullTheWorld_Pitch.pdf` |
| Slide images (1920 x 1080 PNG) | `Presentation/Slides/` |
| Marketing screenshots: 16 portrait 1440 x 2560, 3 landscape 3840 x 2160, 1 mid-journey frame | `Screenshots/` |
| Hero images: cover 1920 x 1080, landscape 2560 x 1440, vertical 1440 x 2560 | `HeroImages/` |

`RawCaptures/` holds the frame sequences, per-shot SFX tracks, stills and intermediates. It is
rebuilt from the project, is not a deliverable, and is git-ignored.

## How it was made

* `Assets/PullTheWorld/Tests/PtwShowcase.cs` drives the game through a shot list
  (`RenderClips`, `RenderMontage`, `RenderStills`): `Time.captureFramerate = 30`, the camera
  rendered by hand into a RenderTexture of the exact output size, the game's own sound effects
  taken through `AudioRenderer` per shot. Music is muted during capture and laid under the cut as
  one continuous piece (the game's own Dawn loop, from "Pull The World/Render Music To WAV").
* `Tools/build_video.js` trims and joins the shots (concatenation for hard cuts, dissolves between
  segments), overlays the logo and the text, mixes SFX with the music bed, and writes both cuts.
* `Tools/compose.ps1` composes the hero images and the slide images (System.Drawing, Poppins from
  the project's fonts).
* `Tools/build_deck.js` wraps the slides into the PPTX and the PDF.

## To re-render

1. In Unity, run the PlayMode tests `PtwShowcase.RenderClips` and `PtwShowcase.RenderStills`
   (`RenderMontage` re-renders only the montage shots). The Editor should be in the foreground.
2. From `Tools/`, with ffmpeg on the PATH (or `FFMPEG` pointing at the exe):

       npm install
       powershell -NoProfile -ExecutionPolicy Bypass -File compose.ps1 all
       node build_deck.js
       node build_video.js

   The edit lives at the top of `build_video.js` (`EDIT`, `TEXTS`, `LOGOS`). `build_video.js`
   writes a review contact sheet to `RawCaptures/_work/master_sheet.png`.
