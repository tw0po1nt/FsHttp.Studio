// Runs the renderer core's JavaScript runtime smoke. It bundles the Fable-compiled
// `Webview.Smoke.run` with the same esbuild that the extension ships with, and then executes the
// bundle under node. `Webview.Smoke.run` asserts each Content-Type dispatch on the *emitted JS*,
// and not on the .NET build. A non-zero exit fails CI.
//
// This guard turns "the core compiles under Fable" into "the core bundles and runs". That
// distinction is what let a StringBuilder that Fable 5.9 cannot bundle pass a green .NET suite.
// This script assumes that `build:fable:webview` has emitted out/webview/Smoke.js.
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
