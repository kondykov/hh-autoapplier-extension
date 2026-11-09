import { build, context } from "esbuild";
import { copyFileSync, existsSync, mkdirSync, readFileSync } from "node:fs";

mkdirSync("dist", { recursive: true });
copyFileSync("manifest.json", "dist/manifest.json");

// Личное письмо лежит в gitignored letter.local.txt и вшивается только в локальную сборку
const defaultLetter = existsSync("letter.local.txt") ? readFileSync("letter.local.txt", "utf8").trim() : "";

// Версия — из manifest.json: метка в панели показывает, какая сборка загружена
const version = JSON.parse(readFileSync("manifest.json", "utf8")).version;

const options = {
  define: { __DEFAULT_LETTER__: JSON.stringify(defaultLetter), __VERSION__: JSON.stringify(version) },
  // content.js — на страницах hh.ru, background.js — service worker для запросов к backend
  entryPoints: { content: "src/content/main.ts", background: "src/background/main.ts" },
  outdir: "dist",
  bundle: true,
  format: "iife",
  target: "chrome120",
  sourcemap: true,
};

if (process.argv.includes("--watch")) {
  const ctx = await context(options);
  await ctx.watch();
} else {
  await build(options);
}

