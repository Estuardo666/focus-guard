import sodium from 'libsodium-wrappers';
import canonicalize from 'canonicalize';
await sodium.ready;
const suite='FG-SODIUM-BOX-1';
const fields=['suite','generationId','keyEpoch','recipientId','recipientPublicKey','membershipDigest','ciphertext','signerDeviceId','signature'];
const bytes=s=>{if(typeof s!=='string'||!/^[A-Za-z0-9_-]+$/.test(s))throw new Error('Invalid encoding');const b=Buffer.from(s,'base64url');if(b.toString('base64url')!==s)throw new Error('Noncanonical encoding');return new Uint8Array(b);};
const b64=b=>Buffer.from(b).toString('base64url');
const signedBytes=head=>Buffer.from(suite+'\0'+canonicalize(head),'utf8');
function requireContext(ctx){if(!Number.isSafeInteger(ctx.keyEpoch)||ctx.keyEpoch<1)throw new Error('Invalid epoch');const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;for(const k of ['accountId','generationId','recipientId','signerDeviceId'])if(typeof ctx[k]!=='string'||!uuid.test(ctx[k]))throw new Error('Invalid context');if(bytes(ctx.membershipDigest).length!==32)throw new Error('Invalid membership digest');}

// Caller must supply context/author key from validated current membership, not untrusted headers.
export function wrapAccountKey(key,ctx,recipientPublicKey,signerPrivateKey){
 requireContext(ctx);if(key.length!==32||recipientPublicKey.length!==32)throw new Error('Invalid key length');
 const payload={protocol:'FG-ACCOUNT-KEY-1',accountId:ctx.accountId,generationId:ctx.generationId,keyEpoch:ctx.keyEpoch,recipientId:ctx.recipientId,membershipDigest:ctx.membershipDigest,contentKey:b64(key)};
 const head={suite,generationId:ctx.generationId,keyEpoch:ctx.keyEpoch,recipientId:ctx.recipientId,recipientPublicKey:b64(recipientPublicKey),membershipDigest:ctx.membershipDigest,ciphertext:b64(sodium.crypto_box_seal(Buffer.from(canonicalize(payload),'utf8'),recipientPublicKey)),signerDeviceId:ctx.signerDeviceId};
 return {...head,signature:b64(sodium.crypto_sign_detached(signedBytes(head),signerPrivateKey))};
}
export function unwrapAccountKey(envelope,ctx,recipientPublicKey,recipientPrivateKey,signerPublicKey){
 requireContext(ctx);
 if(!envelope||Object.keys(envelope).length!==fields.length||fields.some(k=>!Object.hasOwn(envelope,k)))throw new Error('Invalid envelope shape');
 if(envelope.suite!==suite||envelope.recipientPublicKey!==b64(recipientPublicKey))throw new Error('Wrong suite/recipient key');
 for(const k of ['generationId','keyEpoch','recipientId','membershipDigest','signerDeviceId'])if(envelope[k]!==ctx[k])throw new Error('Wrong binding: '+k);
 const {signature,...head}=envelope;
 const sig=bytes(signature), cipher=bytes(envelope.ciphertext);
 if(sig.length!==64||cipher.length<48||cipher.length>6144)throw new Error('Invalid envelope size');
 if(!sodium.crypto_sign_verify_detached(sig,signedBytes(head),signerPublicKey))throw new Error('Author signature rejected');
 const clear=sodium.crypto_box_seal_open(cipher,recipientPublicKey,recipientPrivateKey);
 const text=Buffer.from(clear).toString('utf8'), payload=JSON.parse(text);
 if(!Buffer.from(canonicalize(payload),'utf8').equals(Buffer.from(clear)))throw new Error('Plaintext must be canonical UTF8 JSON (no duplicate keys)');
 const payloadFields=['protocol','accountId','generationId','keyEpoch','recipientId','membershipDigest','contentKey'];
 if(Object.keys(payload).length!==payloadFields.length||payloadFields.some(k=>!Object.hasOwn(payload,k))||payload.protocol!=='FG-ACCOUNT-KEY-1')throw new Error('Invalid key payload');
 for(const k of ['accountId','generationId','keyEpoch','recipientId','membershipDigest'])if(payload[k]!==ctx[k])throw new Error('Wrong encrypted binding: '+k);
 const key=bytes(payload.contentKey);if(key.length!==32)throw new Error('Invalid content key');
 return key;
}
