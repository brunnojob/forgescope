package dev.forgescope.policy;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;

import java.security.KeyFactory;
import java.security.KeyPairGenerator;
import java.security.interfaces.RSAPrivateKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.Base64;
import org.junit.jupiter.api.Test;

class GitHubKeyTest {
    @Test
    void acceptsPkcs8Pem() throws Exception {
        byte[] pkcs8 = key();
        assertArrayEquals(pkcs8, GitHub.privateKeyDer(pem("PRIVATE KEY", pkcs8)));
    }

    @Test
    void wrapsGitHubStylePkcs1Pem() throws Exception {
        byte[] pkcs8 = key();
        byte[] pkcs1 = privateKeyOctets(pkcs8);
        var parsed = (RSAPrivateKey) KeyFactory.getInstance("RSA").generatePrivate(
            new PKCS8EncodedKeySpec(GitHub.privateKeyDer(pem("RSA PRIVATE KEY", pkcs1))));
        var original = (RSAPrivateKey) KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(pkcs8));
        assertEquals(original.getModulus(), parsed.getModulus());
        assertEquals(original.getPrivateExponent(), parsed.getPrivateExponent());
    }

    private static byte[] key() throws Exception {
        var generator = KeyPairGenerator.getInstance("RSA");
        generator.initialize(2048);
        return generator.generateKeyPair().getPrivate().getEncoded();
    }

    private static String pem(String label, byte[] value) {
        return "-----BEGIN " + label + "-----\n"
            + Base64.getMimeEncoder(64, new byte[] {'\n'}).encodeToString(value)
            + "\n-----END " + label + "-----\n";
    }

    private static byte[] privateKeyOctets(byte[] pkcs8) {
        int[] cursor = {0};
        header(pkcs8, cursor, 0x30);
        skip(pkcs8, cursor, 0x02);
        skip(pkcs8, cursor, 0x30);
        return read(pkcs8, cursor, 0x04);
    }

    private static void skip(byte[] value, int[] cursor, int tag) { read(value, cursor, tag); }

    private static byte[] read(byte[] value, int[] cursor, int tag) {
        int length = header(value, cursor, tag);
        byte[] result = java.util.Arrays.copyOfRange(value, cursor[0], cursor[0] + length);
        cursor[0] += length;
        return result;
    }

    private static int header(byte[] value, int[] cursor, int tag) {
        if ((value[cursor[0]++] & 0xff) != tag) throw new IllegalArgumentException("Unexpected DER tag");
        int length = value[cursor[0]++] & 0xff;
        if ((length & 0x80) != 0) {
            int count = length & 0x7f;
            length = 0;
            while (count-- > 0) length = (length << 8) | (value[cursor[0]++] & 0xff);
        }
        return length;
    }
}
