// Bundles the Fable-compiled UI test suite for a standalone Mocha run under Node.
import * as esbuild from "esbuild";
import { fileURLToPath } from "node:url";
import path from "node:path";

const dir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(dir, "../..");
const entry = path.join(repoRoot, "out", "ui-tests", "ScaffoldTests.js");
const outfile = path.join(repoRoot, "out", "ui-tests", "suite.bundle.cjs");

await esbuild.build({
  entryPoints: [entry],
  outfile,
  bundle: true,
  platform: "node",
  target: "node18",
  format: "cjs",
  external: ["vscode"],
  sourcemap: true,
});
