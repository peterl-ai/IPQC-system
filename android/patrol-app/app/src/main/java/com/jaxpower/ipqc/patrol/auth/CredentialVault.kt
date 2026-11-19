package com.jaxpower.ipqc.patrol.auth

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import java.io.File
import java.io.FileNotFoundException
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

// Regular classes avoid putting any credential into a generated toString().
@Serializable
class StoredRefresh(
    val origin: String,
    val refreshToken: String,
    val userId: String,
    val familyId: String,
    val environment: String,
    val expiresAtUtc: String,
)

interface CredentialVault {
    fun read(): StoredRefresh?
    fun replace(record: StoredRefresh)
    fun clear()
}

class KeystoreCredentialVault(
    context: Context,
    fileName: String = "personal-session.v1",
    private val keyAlias: String = "ipqc.personal-session.v1",
) : CredentialVault {
    private val encrypted = AtomicFile(File(context.noBackupFilesDir, fileName))

    private fun keys() = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    private fun createKey(): SecretKey = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        .apply {
            init(KeyGenParameterSpec.Builder(keyAlias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .setRandomizedEncryptionRequired(true)
                .build())
        }
        .generateKey()

    override fun read(): StoredRefresh? {
        val bytes = try {
            encrypted.openRead().use { input ->
                require(input.channel.size() in (1L + IV_SIZE + 16)..MAX_SIZE.toLong())
                input.readBytes()
            }
        } catch (missing: FileNotFoundException) {
            if (encrypted.baseFile.exists()) throw missing
            return null
        }
        require(bytes[0] == FORMAT)
        val key = keys().getKey(keyAlias, null) as? SecretKey ?: error("Credential key unavailable")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, bytes.copyOfRange(1, 1 + IV_SIZE)))
        cipher.updateAAD(byteArrayOf(FORMAT))
        val plaintext = cipher.doFinal(bytes, 1 + IV_SIZE, bytes.size - 1 - IV_SIZE)
        return try {
            Json.decodeFromString<StoredRefresh>(plaintext.decodeToString(throwOnInvalidSequence = true))
        } finally {
            plaintext.fill(0)
        }
    }

    override fun replace(record: StoredRefresh) {
        val key = keys().getKey(keyAlias, null) as? SecretKey ?: createKey()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key)
        check(cipher.iv.size == IV_SIZE)
        cipher.updateAAD(byteArrayOf(FORMAT))
        val plaintext = Json.encodeToString(record).encodeToByteArray()
        val bytes = try {
            byteArrayOf(FORMAT) + cipher.iv + cipher.doFinal(plaintext)
        } finally {
            plaintext.fill(0)
        }
        require(bytes.size <= MAX_SIZE)
        val output = encrypted.startWrite()
        try {
            output.write(bytes)
            encrypted.finishWrite(output)
        } catch (e: Exception) {
            encrypted.failWrite(output)
            throw e
        }
        // AtomicFile may log a failed rename instead of throwing. Confirm durable replacement.
        check(encrypted.openRead().use { it.readBytes().contentEquals(bytes) })
    }

    override fun clear() {
        // Delete the key first so an interrupted delete cannot leave a reusable refresh token.
        val keyRemoved = runCatching {
            val store = keys()
            store.deleteEntry(keyAlias)
            !store.containsAlias(keyAlias)
        }.getOrDefault(false)
        encrypted.delete()
        val path = encrypted.baseFile.path
        check(keyRemoved || listOf(path, "$path.bak", "$path.new").none { File(it).exists() })
    }

    private companion object {
        const val FORMAT: Byte = 1
        const val IV_SIZE = 12
        const val MAX_SIZE = 32 * 1024
    }
}
