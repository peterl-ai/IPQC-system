package com.jaxpower.ipqc.patrol.offline

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

internal interface ReceiptStorage {
    fun read(): ByteArray?
    fun replace(bytes: ByteArray)
    fun revoke()
}

internal class ProtectedReceipt(file: File, private val box: KeystoreBox) : ReceiptStorage {
    private val atomic = AtomicFile(file)

    override fun read(): ByteArray? {
        val bytes = try {
            atomic.openRead().use { input ->
                require(input.channel.size() in 1L..MAX_RECEIPT_BYTES.toLong())
                input.readBytes()
            }
        } catch (e: FileNotFoundException) {
            if (variants().any { it.exists() }) throw e
            return null
        }
        return box.open(RECEIPT_DOMAIN, bytes)
    }

    override fun replace(bytes: ByteArray) {
        val sealed = box.seal(RECEIPT_DOMAIN, bytes, allowNewKey = true)
        require(sealed.size <= MAX_RECEIPT_BYTES)
        val output = atomic.startWrite()
        try {
            output.write(sealed)
            atomic.finishWrite(output)
        } catch (e: Exception) {
            atomic.failWrite(output)
            throw e
        }
        check(atomic.openRead().use { it.readBytes().contentEquals(sealed) })
    }

    override fun revoke() {
        // A crash after successful key deletion cannot restore the old admission.
        val removed = runCatching { box.removeKey() }.getOrDefault(false)
        atomic.delete()
        check(removed || variants().none { it.exists() })
    }

    private fun variants() = listOf(atomic.baseFile, File(atomic.baseFile.path + ".bak"), File(atomic.baseFile.path + ".new"))

    private companion object {
        const val MAX_RECEIPT_BYTES = 32 * 1024
        const val RECEIPT_DOMAIN = "ipqc-offline-admission"
    }
}

internal interface CacheCipher {
    fun seal(slot: String, plaintext: ByteArray, allowNewKey: Boolean = false): ByteArray
    fun open(slot: String, sealed: ByteArray): ByteArray
}

internal class KeystoreBox(private val alias: String) : CacheCipher {
    override fun seal(slot: String, plaintext: ByteArray, allowNewKey: Boolean): ByteArray {
        val key = currentKey() ?: if (allowNewKey) createKey() else error("Encryption key unavailable")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key)
        check(cipher.iv.size == IV_SIZE)
        cipher.updateAAD(aad(slot))
        return byteArrayOf(FORMAT) + cipher.iv + cipher.doFinal(plaintext)
    }

    override fun open(slot: String, sealed: ByteArray): ByteArray {
        require(sealed.size >= 1 + IV_SIZE + TAG_SIZE && sealed[0] == FORMAT)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val key = currentKey() ?: error("Encryption key unavailable")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_SIZE * 8, sealed.copyOfRange(1, 1 + IV_SIZE)))
        cipher.updateAAD(aad(slot))
        return cipher.doFinal(sealed, 1 + IV_SIZE, sealed.size - 1 - IV_SIZE)
    }

    fun removeKey(): Boolean = keyStore().let { keys ->
        keys.deleteEntry(alias)
        !keys.containsAlias(alias)
    }

    private fun aad(slot: String) = byteArrayOf(FORMAT) + slot.encodeToByteArray()

    private fun keyStore() = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    private fun currentKey() = keyStore().getKey(alias, null) as? SecretKey

    private fun createKey(): SecretKey = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        .apply {
            init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .setRandomizedEncryptionRequired(true)
                .build())
        }
        .generateKey()

    private companion object {
        const val FORMAT: Byte = 1
        const val IV_SIZE = 12
        const val TAG_SIZE = 16
    }
}
