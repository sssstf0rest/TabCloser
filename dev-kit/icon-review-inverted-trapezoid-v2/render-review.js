const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');

const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const outputDirectory = __dirname;

async function renderIconSet() {
  const svg = await fs.readFile(path.join(outputDirectory, 'master.svg'));
  const master = await sharp(svg, { density: 384 })
    .resize(1024, 1024, { fit: 'fill' })
    .png({ compressionLevel: 9 })
    .toBuffer();

  await fs.writeFile(path.join(outputDirectory, 'master-1024.png'), master);

  const rendered = new Map();
  for (const size of sizes) {
    const buffer = await sharp(master)
      .resize(size, size, { fit: 'fill', kernel: sharp.kernel.lanczos3 })
      .png({ compressionLevel: 9 })
      .toBuffer();

    rendered.set(size, buffer);
    await fs.writeFile(path.join(outputDirectory, `icon-${size}.png`), buffer);
  }

  const columnWidth = 140;
  const sheetWidth = 60 + (columnWidth * sizes.length);
  const sheetHeight = 360;
  const columns = sizes.map((size, index) => {
    const x = 30 + (index * columnWidth);
    return `
      <rect x="${x}" y="55" width="120" height="120" rx="12" fill="#F8FAFC"/>
      <rect x="${x}" y="195" width="120" height="120" rx="12" fill="#202832"/>
      <text x="${x + 60}" y="342" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="16" fill="#17202A">${size} px</text>`;
  }).join('');

  const sheetBackground = Buffer.from(`
    <svg xmlns="http://www.w3.org/2000/svg" width="${sheetWidth}" height="${sheetHeight}">
      <rect width="100%" height="100%" fill="#E8EDF2"/>
      <text x="30" y="34" font-family="Segoe UI, sans-serif" font-size="20" font-weight="600" fill="#17202A">Inverted trapezoid icon — pixel previews enlarged to 96 px</text>
      ${columns}
    </svg>`);

  const composites = [];
  for (const [index, size] of sizes.entries()) {
    const x = 42 + (index * columnWidth);
    const kernel = size < 96 ? sharp.kernel.nearest : sharp.kernel.lanczos3;
    const preview = await sharp(rendered.get(size))
      .resize(96, 96, { fit: 'fill', kernel })
      .png()
      .toBuffer();

    composites.push({ input: preview, left: x, top: 67 });
    composites.push({ input: preview, left: x, top: 207 });
  }

  await sharp(sheetBackground)
    .composite(composites)
    .png({ compressionLevel: 9 })
    .toFile(path.join(outputDirectory, 'preview-all-sizes.png'));
}

renderIconSet().catch((error) => {
  process.stderr.write(`${error.stack}\n`);
  process.exitCode = 1;
});
