const fs = require('node:fs/promises');
const path = require('node:path');

const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

async function packageIcon() {
  const outputPath = process.argv[2];
  if (!outputPath) {
    throw new Error('Usage: node package-ico.js <output.ico>');
  }

  const images = await Promise.all(sizes.map(async (size) => {
    const inputPath = path.join(__dirname, `icon-${size}.png`);
    const data = await fs.readFile(inputPath);
    const width = data.readUInt32BE(16);
    const height = data.readUInt32BE(20);

    if (width !== size || height !== size) {
      throw new Error(`${inputPath} is ${width}x${height}; expected ${size}x${size}`);
    }

    return { size, data };
  }));

  const directorySize = 6 + (16 * images.length);
  const header = Buffer.alloc(directorySize);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);

  let imageOffset = directorySize;
  images.forEach(({ size, data }, index) => {
    const entryOffset = 6 + (16 * index);
    header.writeUInt8(size === 256 ? 0 : size, entryOffset);
    header.writeUInt8(size === 256 ? 0 : size, entryOffset + 1);
    header.writeUInt8(0, entryOffset + 2);
    header.writeUInt8(0, entryOffset + 3);
    header.writeUInt16LE(1, entryOffset + 4);
    header.writeUInt16LE(32, entryOffset + 6);
    header.writeUInt32LE(data.length, entryOffset + 8);
    header.writeUInt32LE(imageOffset, entryOffset + 12);
    imageOffset += data.length;
  });

  await fs.writeFile(outputPath, Buffer.concat([header, ...images.map(({ data }) => data)]));
}

packageIcon().catch((error) => {
  process.stderr.write(`${error.stack}\n`);
  process.exitCode = 1;
});
