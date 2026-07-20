// Runs the renderer core's JavaScript runtime smoke: bundle the Fable-compiled `Webview.Smoke.run`
// (which asserts each Content-Type dispatch on the *emitted JS*, not the .NET build) with the same
// esbuild the extension ships with, then execute it under node. A non-zero exit fails CI.
//
// This is the guard that turns "the core compiles under Fable" into "the core actually bundles and
// runs" — the distinction that let a StringBuilder Fable 5.9 can't bundle slip past a green .NET
// suite. Assumes `build:fable:webview` has emitted out/webview/Smoke.js.
import * as esbuild from "esbuild";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const dir = path.dirname(fileURLToPath(import.meta.url));
const outfile = path.join(dir, "out", "webview", "smoke.bundle.mjs");

await esbuild.build({
  stdin: {
    contents: `import { run } from "./out/webview/Smoke.js";\nrun();\n`,
    resolveDir: dir,
    sourcefile: "smoke-entry.mjs",
  },
  bundle: true,
  platform: "node",
  format: "esm",
  outfile,
});

const result = spawnSync(process.execPath, [outfile], { stdio: "inherit" });
process.exit(result.status ?? 1);
