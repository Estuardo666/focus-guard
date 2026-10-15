import test from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import sodium from 'libsodium-wrappers';
await sodium.ready;
const project=fileURLToPath(new URL('./CryptoProbe/CryptoProbe.csproj',import.meta.url));
const result=execFileSync('dotnet',['run','--project',project,'--configuration','Release','--no-build'],{encoding:'utf8',timeout:60000});
const vector=JSON.parse(result.trim());
const bytes=s=>Uint8Array.from(Buffer.from(s,'hex'));
test('NSec 26.4.0 and libsodium-wrappers 0.8.4 produce identical XChaCha20 ciphertext',()=>{
 const cipher=sodium.crypto_aead_xchacha20poly1305_ietf_encrypt(bytes(vector.message),bytes(vector.aad),null,bytes(vector.nonce),bytes(vector.symmetricKey));
 assert.deepEqual(Buffer.from(cipher),Buffer.from(vector.ciphertext,'hex'));
 assert.deepEqual(Buffer.from(sodium.crypto_aead_xchacha20poly1305_ietf_decrypt(null,bytes(vector.ciphertext),bytes(vector.aad),bytes(vector.nonce),bytes(vector.symmetricKey))),Buffer.from(vector.message,'hex'));
});
test('Changed ciphertext, AAD, nonce or key cannot decrypt',()=>{
 for(const field of ['ciphertext','aad','nonce','symmetricKey']){
  const v=structuredClone(vector),modified=bytes(v[field]);modified[0]^=1;v[field]=Buffer.from(modified).toString('hex');
  assert.throws(()=>sodium.crypto_aead_xchacha20poly1305_ietf_decrypt(null,bytes(v.ciphertext),bytes(v.aad),bytes(v.nonce),bytes(v.symmetricKey)),field);
 }
});
test('NSec and libsodium produce identical Ed25519 keys and signatures from public seed',()=>{
 const kp=sodium.crypto_sign_seed_keypair(bytes(vector.signingSeed));
 assert.deepEqual(Buffer.from(kp.publicKey),Buffer.from(vector.signingPublicKey,'hex'));
 assert.deepEqual(Buffer.from(sodium.crypto_sign_detached(bytes(vector.message),kp.privateKey)),Buffer.from(vector.signature,'hex'));
 assert.equal(sodium.crypto_sign_verify_detached(bytes(vector.signature),bytes(vector.message),kp.publicKey),true);
 const changed=bytes(vector.message);changed[0]^=1;
 assert.equal(sodium.crypto_sign_verify_detached(bytes(vector.signature),changed,kp.publicKey),false);
});
test('Documented libsodium sealed boxes interoperate C# native binding to Node in both directions',()=>{
 const kp=sodium.crypto_box_seed_keypair(bytes(vector.signingSeed));
 assert.deepEqual(Buffer.from(kp.publicKey),Buffer.from(vector.sealedPublicKey,'hex'));
 assert.deepEqual(Buffer.from(sodium.crypto_box_seal_open(bytes(vector.sealedCiphertext),kp.publicKey,kp.privateKey)),Buffer.from(vector.message,'hex'));
 const fromNode=sodium.crypto_box_seal(bytes(vector.message),kp.publicKey);
 const opened=execFileSync('dotnet',['run','--project',project,'--configuration','Release','--no-build','--','--open-sealed'],{input:Buffer.from(fromNode).toString('hex')+'\n',encoding:'utf8',timeout:60000});
 assert.deepEqual(Buffer.from(opened.trim(),'hex'),Buffer.from(vector.message,'hex'));
});
test('Sealed-box tampering is rejected instead of returning a key',()=>{
 const kp=sodium.crypto_box_seed_keypair(bytes(vector.signingSeed));
 const altered=bytes(vector.sealedCiphertext);altered[altered.length-1]^=1;
 assert.throws(()=>sodium.crypto_box_seal_open(altered,kp.publicKey,kp.privateKey));
});

