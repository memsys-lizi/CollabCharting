import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const resourceRoot = join(process.cwd(), "data", "resources");

function safePart(value: string) {
  if (!/^[a-zA-Z0-9_-]+$/.test(value)) {
    throw new Error("资源路径参数无效。");
  }

  return value;
}

export async function saveResource(roomId: string, sha256: string, bytes: Buffer) {
  const expected = safePart(sha256).toUpperCase();
  const actual = createHash("sha256").update(bytes).digest("hex").toUpperCase();
  if (actual !== expected) {
    throw new Error("资源 sha256 校验失败。");
  }

  const dir = join(resourceRoot, safePart(roomId));
  await mkdir(dir, { recursive: true });
  await writeFile(join(dir, expected), bytes);
}

export async function loadResource(roomId: string, sha256: string) {
  const file = join(resourceRoot, safePart(roomId), safePart(sha256).toUpperCase());
  return readFile(file);
}
