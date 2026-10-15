import test from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import sodium from 'libsodium-wrappers';
import {wrapAccountKey,unwrapAccountKey} from './key-envelope.mjs';
await sodium.ready;
const id='11111111-1111-4111-8111-111111111111';
const ctx={accountId:id,generationId:id,keyEpoch:1,recipientId:id,membershipDigest:'A'.repeat(43),signerDeviceId:id};
const recipient=sodium.crypto_box_seed_keypair(Uint8Array.from({length:32},(_,i)=>i+33));
const author=sodium.crypto_sign_seed_keypair(Uint8Array.from({length:32},(_,i)=>i+65));
const key=new Uint8Array(32).fill(42);
const encode=b=>Buffer.from(b).toString('base64url');
const make=()=>wrapAccountKey(key,ctx,recipient.publicKey,author.privateKey);
const open=(e,c=ctx,r=recipient,a=author)=>unwrapAccountKey(e,c,r.publicKey,r.privateKey,a.publicKey);
test('Signed key envelope roundtrip keeps account key and current membership binding',()=>assert.deepEqual(open(make()),key));
test('Old epoch and valid-but-different generation/account/recipient/membership/author contexts reject',()=>{for(const name of ['accountId','generationId','recipientId','signerDeviceId'])assert.throws(()=>open(make(),{...ctx,[name]:'22222222-2222-4222-8222-222222222222'}),name);assert.throws(()=>open(make(),{...ctx,membershipDigest:Buffer.alloc(32,1).toString('base64url')}));assert.throws(()=>open(make(),{...ctx,keyEpoch:2}));});
test('Forged signature, changed header/ciphertext and unknown field reject',()=>{const original=make(),tampered=Buffer.from(original.ciphertext,'base64url');tampered[0]^=1;for(const e of [{...original,signature:'A'.repeat(86)},{...original,ciphertext:tampered.toString('base64url')},{...original,recipientId:'wrong'},{...original,secret:'extra'}])assert.throws(()=>open(e));});
test('Unrelated author and recipient keys cannot unwrap',()=>{const stranger=sodium.crypto_sign_keypair();assert.throws(()=>open(make(),ctx,recipient,stranger));const other=sodium.crypto_box_keypair();assert.throws(()=>open(make(),ctx,other));});
test('Wrong-sized content key or unsafe epoch cannot be published',()=>{assert.throws(()=>wrapAccountKey(new Uint8Array(31),ctx,recipient.publicKey,author.privateKey));assert.throws(()=>wrapAccountKey(key,{...ctx,keyEpoch:2**53},recipient.publicKey,author.privateKey));});
test('C# native sealed-box probe decrypts the same canonical bound payload',()=>{
 const envelope=make(), project=fileURLToPath(new URL('./CryptoProbe/CryptoProbe.csproj',import.meta.url));
 const hex=Buffer.from(envelope.ciphertext,'base64url').toString('hex');
 const clear=execFileSync('dotnet',['run','--project',project,'-c','Release','--no-build','--','--open-sealed'],{input:hex+'\n',encoding:'utf8',timeout:60000});
 const payload=JSON.parse(Buffer.from(clear.trim(),'hex').toString('utf8'));
 assert.equal(payload.accountId,ctx.accountId);assert.equal(payload.keyEpoch,1);assert.deepEqual(Buffer.from(payload.contentKey,'base64url'),Buffer.from(key));
});
test('production C# codec and libsodium-wrappers exchange signed key envelopes both ways',()=>{
 const project=fileURLToPath(new URL('./DeviceInterop/FocusGuard.Devices.InteropProbe.csproj',import.meta.url));
 const common={accountId:ctx.accountId,generationId:ctx.generationId,keyEpoch:ctx.keyEpoch,recipientId:ctx.recipientId,
  membershipDigest:ctx.membershipDigest,signerDeviceId:ctx.signerDeviceId,recipientDeviceId:ctx.recipientId,
  signerSigningSeed:encode(Uint8Array.from({length:32},(_,i)=>i+65)),
  signerBoxSeed:encode(Uint8Array.from({length:32},(_,i)=>i+97)),
  recipientSigningSeed:encode(Uint8Array.from({length:32},(_,i)=>i+129)),
  recipientBoxSeed:encode(Uint8Array.from({length:32},(_,i)=>i+161)),contentKey:encode(key)};
 const interopRecipient=sodium.crypto_box_seed_keypair(Uint8Array.from({length:32},(_,i)=>i+161));
 const csharpOpensNode=wrapAccountKey(key,ctx,interopRecipient.publicKey,author.privateKey);
 const first=execFileSync('dotnet',['run','--project',project,'-c','Release','--no-build'],
  {input:JSON.stringify({...common,mode:'node-to-csharp',envelope:csharpOpensNode}),encoding:'utf8',timeout:60000});
 assert.match(first,/"status":"PASS"/);
 const emitted=execFileSync('dotnet',['run','--project',project,'-c','Release','--no-build'],
  {input:JSON.stringify({...common,mode:'csharp-to-node'}),encoding:'utf8',timeout:60000});
 const result=JSON.parse(emitted);
 assert.equal(result.status,'PASS');
 const nodeRecipient=sodium.crypto_box_seed_keypair(Uint8Array.from({length:32},(_,i)=>i+161));
 const nodeAuthor=sodium.crypto_sign_seed_keypair(Uint8Array.from({length:32},(_,i)=>i+65));
 assert.equal(encode(nodeRecipient.publicKey),result.recipient.encryptionPublicKey);
 assert.equal(encode(nodeAuthor.publicKey),result.signer.signingPublicKey);
 assert.deepEqual(Buffer.from(unwrapAccountKey(result.envelope,ctx,nodeRecipient.publicKey,nodeRecipient.privateKey,nodeAuthor.publicKey)),Buffer.from(key));
});
