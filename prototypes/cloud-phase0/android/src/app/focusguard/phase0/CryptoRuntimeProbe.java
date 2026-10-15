package app.focusguard.phase0;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.ptr.LongByReference;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/** Public deterministic vectors only; no production keys, account or network. */
public final class CryptoRuntimeProbe {
    private CryptoRuntimeProbe() { }
    public interface Sodium extends Library {
        int sodium_init();
        Pointer sodium_version_string();
        int crypto_aead_xchacha20poly1305_ietf_encrypt(byte[] c, LongByReference clen, byte[] m, long mlen,
                byte[] ad, long adlen, Pointer nsec, byte[] nonce, byte[] key);
        int crypto_aead_xchacha20poly1305_ietf_decrypt(byte[] m, LongByReference mlen, Pointer nsec,
                byte[] c, long clen, byte[] ad, long adlen, byte[] nonce, byte[] key);
        int crypto_sign_seed_keypair(byte[] pk, byte[] sk, byte[] seed);
        int crypto_sign_detached(byte[] signature, LongByReference siglen, byte[] m, long mlen, byte[] sk);
        int crypto_sign_verify_detached(byte[] signature, byte[] m, long mlen, byte[] pk);
        int crypto_box_seed_keypair(byte[] pk, byte[] sk, byte[] seed);
        int crypto_box_seal(byte[] cipher, byte[] message, long messageLength, byte[] pk);
        int crypto_box_seal_open(byte[] message, byte[] cipher, long cipherLength, byte[] pk, byte[] sk);
    }
    public static String run() {
        Sodium s=Native.load("sodium",Sodium.class);
        require(s.sodium_init()>=0,"init");
        String version=s.sodium_version_string().getString(0,"UTF-8");
        require("1.0.22".equals(version),"Unexpected native version: "+version);
        try {
            require(new org.webpki.jcs.JsonCanonicalizer("{\"z\":1,\"a\":\"café\",\"n\":1e30}").getEncodedString()
                    .equals("{\"a\":\"café\",\"n\":1e+30,\"z\":1}"),"JCS Unicode/order differs");
            require(new org.webpki.jcs.JsonCanonicalizer("{\"numbers\":[333333333.33333329,1e30,4.5,0.002,1e-27],\"literals\":[null,true,false]}").getEncodedString()
                    .equals("{\"literals\":[null,true,false],\"numbers\":[333333333.3333333,1e+30,4.5,0.002,1e-27]}"),"JCS numbers differ");
            boolean duplicateRejected=false;
            try { new org.webpki.jcs.JsonCanonicalizer("{\"a\":1,\"a\":2}"); }
            catch (java.io.IOException expected) { duplicateRejected=true; }
            require(duplicateRejected,"Duplicate JSON accepted");
        } catch (java.io.IOException ex) { throw new IllegalStateException("JCS parsing failed",ex); }
        byte[] key=range(1,32),nonce=range(1,24),seed=range(33,32);
        byte[] message="FocusGuard prueba pública: café y enfoque.".getBytes(StandardCharsets.UTF_8);
        byte[] aad="FG-E2EE-1:test-generation:test-entity:1".getBytes(StandardCharsets.UTF_8);
        byte[] cipher=new byte[message.length+16];LongByReference len=new LongByReference();
        require(s.crypto_aead_xchacha20poly1305_ietf_encrypt(cipher,len,message,message.length,aad,aad.length,Pointer.NULL,nonce,key)==0,"encrypt");
        require(len.getValue()==cipher.length,"cipher length");
        require(hex(cipher).equals("F3B7A4FB600577D4175A5B1FB64B155C5D9B324E3F63079A2C7F8F4C98B450DC063960BF6A7DFA15EF48A52A9700978AD4857FD49CEA6A0B9A5AB1BF"),"cipher differs from C#/Node vector");
        byte[] plain=new byte[message.length];
        require(s.crypto_aead_xchacha20poly1305_ietf_decrypt(plain,len,Pointer.NULL,cipher,cipher.length,aad,aad.length,nonce,key)==0,"decrypt");
        require(Arrays.equals(plain,message),"message mismatch");
        byte[] badAad=aad.clone();badAad[0]^=1;
        require(s.crypto_aead_xchacha20poly1305_ietf_decrypt(plain,len,Pointer.NULL,cipher,cipher.length,badAad,badAad.length,nonce,key)!=0,"tampering accepted");
        byte[] pk=new byte[32],sk=new byte[64],sig=new byte[64];
        require(s.crypto_sign_seed_keypair(pk,sk,seed)==0,"sign keys");
        require(s.crypto_sign_detached(sig,len,message,message.length,sk)==0,"sign");
        require(hex(sig).equals("B9B78395BABEFC2E25D656071B85FB0718139F3F2FC68B880408C6B49FB60C3F92982BC5AC151EF6CC8C7F8C8F6762C8CC2DDAF769038EDD31775F43C82BE800"),"signature differs from C#/Node vector");
        require(s.crypto_sign_verify_detached(sig,message,message.length,pk)==0,"verify");
        byte[] boxPk=new byte[32],boxSk=new byte[32],sealed=new byte[message.length+48],opened=new byte[message.length];
        require(s.crypto_box_seed_keypair(boxPk,boxSk,seed)==0,"box keys");
        require(s.crypto_box_seal(sealed,message,message.length,boxPk)==0,"seal");
        require(s.crypto_box_seal_open(opened,sealed,sealed.length,boxPk,boxSk)==0,"open sealed");
        require(Arrays.equals(opened,message),"sealed plaintext");
        sealed[sealed.length-1]^=1;
        require(s.crypto_box_seal_open(opened,sealed,sealed.length,boxPk,boxSk)!=0,"sealed tampering accepted");
        Arrays.fill(boxSk,(byte)0);
        Arrays.fill(sk,(byte)0);Arrays.fill(key,(byte)0);
        return "CRYPTO_PROBE_PASS · libsodium "+version+"\nAEAD/firma coinciden con C#/Node; AAD alterado rechazado.\nSealed-box roundtrip y rechazo de alteración pasan.\nJCS Unicode/números/orden y rechazo de duplicados pasan.\nSolo vectores públicos de ensayo; no bloqueo ni cuenta.";
    }
    private static byte[] range(int first,int n){byte[] result=new byte[n];for(int i=0;i<n;i++)result[i]=(byte)(first+i);return result;}
    private static String hex(byte[] data){char[] chars="0123456789ABCDEF".toCharArray();StringBuilder b=new StringBuilder();for(byte v:data){b.append(chars[(v&255)>>>4]);b.append(chars[v&15]);}return b.toString();}
    private static void require(boolean ok,String check){if(!ok)throw new IllegalStateException(check);}
}
