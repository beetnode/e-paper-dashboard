/**
 * Extracts Font Awesome solid SVG icon paths into a compact JSON file.
 * Output is placed in public/ so Angular includes it as a static asset.
 *
 * Format: { "icon-name": { "path": "M...", "vbW": 512, "vbH": 512 }, ... }
 * Also writes public/fa-icon-names.json, a sorted list of the same icon names.
 */
const fs = require('fs');
const path = require('path');

const svgDir = path.join(__dirname, 'node_modules', '@fortawesome', 'fontawesome-free', 'svgs', 'solid');
const outFile = path.join(__dirname, 'public', 'fa-icons.json');

if (!fs.existsSync(svgDir)) {
  console.error('Font Awesome SVG directory not found:', svgDir);
  process.exit(1);
}

const icons = {};
const files = fs.readdirSync(svgDir).filter(f => f.endsWith('.svg'));

for (const file of files) {
  const name = file.replace('.svg', '');
  const svg = fs.readFileSync(path.join(svgDir, file), 'utf8');

  // Extract viewBox
  const vbMatch = svg.match(/viewBox="0 0 (\d+) (\d+)"/);
  if (!vbMatch) continue;

  // Extract path d attribute (take the first/main path)
  const pathMatch = svg.match(/<path[^>]*\bd="([^"]+)"/);
  if (!pathMatch) continue;

  icons[name] = {
    path: pathMatch[1],
    vbW: parseInt(vbMatch[1], 10),
    vbH: parseInt(vbMatch[2], 10),
  };
}

fs.writeFileSync(outFile, JSON.stringify(icons), 'utf8');
console.log(`Generated ${Object.keys(icons).length} FA icon entries → ${outFile}`);

// Names only, for the designer's icon picker (the full file is ~1 MB of path data).
const namesFile = path.join(__dirname, 'public', 'fa-icon-names.json');
fs.writeFileSync(namesFile, JSON.stringify(Object.keys(icons).sort()), 'utf8');
console.log(`Generated icon name list → ${namesFile}`);
