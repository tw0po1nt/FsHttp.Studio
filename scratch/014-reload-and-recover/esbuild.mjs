import * as esbuild from "esbuild";
import { mkdirSync } from "node:fs";

mkdirSync("out", { recursive: true });

await esbuild.build({
  entryPoints: ["out/fable/ReloadRecover.js"],
  bundle: true,
  platform: "node",
  format: "cjs",
  outfile: "out/reload-recover.test.js",
  sourcemap: true,
  external: ["vscode-extension-tester", "selenium-webdriver", "mocha"],
});

console.log("bundled out/reload-recover.test.js");
