import { createCipheriv, createDecipheriv, createHash, randomBytes } from 'node:crypto';

/**
 * AES-256-GCM with a SHA-256 derived key. Encrypted payload format (base64):
 *   [12-byte IV][16-byte auth tag][ciphertext]
 */
export function encryptString(plain: string, key: string): string {
  const derivedKey = deriveKey(key);
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', derivedKey, iv);
  const enc = Buffer.concat([cipher.update(plain, 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return Buffer.concat([iv, tag, enc]).toString('base64');
}

export function decryptString(payload: string, key: string): string {
  const buf = Buffer.from(payload, 'base64');
  if (buf.length < 12 + 16) throw new Error('ciphertext too short');
  const iv = buf.subarray(0, 12);
  const tag = buf.subarray(12, 28);
  const data = buf.subarray(28);
  const decipher = createDecipheriv('aes-256-gcm', deriveKey(key), iv);
  decipher.setAuthTag(tag);
  const dec = Buffer.concat([decipher.update(data), decipher.final()]);
  return dec.toString('utf8');
}

export function sha256Hex(data: string | Buffer): string {
  return createHash('sha256').update(data).digest('hex');
}

function deriveKey(key: string): Buffer {
  return createHash('sha256').update(key, 'utf8').digest();
}
