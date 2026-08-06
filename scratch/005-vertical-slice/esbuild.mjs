import * as esbuild from "esbuild";
import { mkdirSync } from "node:fs";

mkdirSync("out", { recursive: true });

await esbuild.build({
  entryPoints: ["out/fable/CorePath.js"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "out/core-path.test.js",
  sourcemap: true,
  external: [
    "vscode-extension-tester",
    "selenium-webdriver",
    "mocha",
  ],
});

console.log("bundled out/core-path.test.js");
