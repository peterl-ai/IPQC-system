package com.jaxpower.ipqc.patrol

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.auth.KeystoreCredentialVault
import com.jaxpower.ipqc.patrol.auth.StoredRefresh
import java.io.File
import java.util.UUID
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class KeystoreCredentialVaultTest {
    @Test fun refreshIsEncryptedRotatedAndClearedThroughPlatformKeystore() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val id = "ipqc.fixture.${UUID.randomUUID()}"
        val file = File(context.noBackupFilesDir, id)
        val vault = KeystoreCredentialVault(context, id, id)
        fun record(token: String) = StoredRefresh("https://test.example.invalid/", token, "test-user", "test-family",
            "test-environment", "2026-11-26T14:00:00Z")
        try {
            vault.replace(record("test-only-token-before"))
            assertFalse(file.readBytes().toString(Charsets.UTF_8).contains("test-only-token-before"))
            assertEquals("test-only-token-before", KeystoreCredentialVault(context, id, id).read()?.refreshToken)
            vault.replace(record("test-only-token-after"))
            assertEquals("test-only-token-after", vault.read()?.refreshToken)
            val tampered = file.readBytes()
            tampered[tampered.lastIndex] = (tampered.last().toInt() xor 1).toByte()
            file.writeBytes(tampered)
            assertThrows(Exception::class.java) { vault.read() }
            vault.clear()
            assertNull(vault.read())
            assertFalse(file.exists())
        } finally {
            vault.clear()
        }
    }
}
