// ADR-0005: esbuild bundles two separate entries. The first is the extension host, with a Node
// target and `vscode` marked external. The second is the webview script, with a browser target.
import * as esbuild from "esbuild";

const watch = process.argv.includes("--watch");

const hostConfig = {
  entryPoints: ["out/Extension.js"],
  outfile: "dist/extension.js",
  bundle: true,
  platform: "node",
  target: "node18",
  format: "cjs",
  external: ["vscode"],
  sourcemap: true,
};

const webviewConfig = {
  entryPoints: ["out/webview/Main.js"],
  outfile: "dist/webview/main.js",
  bundle: true,
  platform: "browser",
  format: "iife",
  sourcemap: true,
};

async function run() {
  if (watch) {
    const [hostCtx, webviewCtx] = await Promise.all([
      esbuild.context(hostConfig),
      esbuild.context(webviewConfig),
    ]);
    await Promise.all([hostCtx.watch(), webviewCtx.watch()]);
    console.log("esbuild watching...");
  } else {
    await Promise.all([esbuild.build(hostConfig), esbuild.build(webviewConfig)]);
  }
}

run().catch((err) => {
  console.error(err);
  process.exit(1);
});
