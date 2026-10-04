// Writes deploy/security-headers.json as nginx add_header lines. The same JSON feeds `vite preview`,
// so the end-to-end tests run under exactly the headers the web container sends.
import { readFileSync } from 'node:fs'

const headers = JSON.parse(readFileSync(new URL('./security-headers.json', import.meta.url), 'utf8'))
for (const [name, value] of Object.entries(headers)) {
  if (/["\\\n]/.test(value)) throw new Error(`Header ${name} contains a character nginx would misread`)
  console.log(`add_header ${name} "${value}" always;`)
}
