package com.jaxpower.ipqc.patrol.offline

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.Transaction
import androidx.room.withTransaction
import java.io.File

// Room and its journals receive only opaque slots and chunks of already sealed ciphertext.
@Entity(tableName = "cache_chunk", primaryKeys = ["slot", "part"])
internal data class CacheChunk(val slot: String, val part: Int, val ciphertext: ByteArray)

@Dao
internal abstract class CacheDao {
    @Query("SELECT * FROM cache_chunk WHERE slot = :slot ORDER BY part")
    abstract suspend fun read(slot: String): List<CacheChunk>

    @Query("SELECT COUNT(*) FROM cache_chunk")
    abstract suspend fun count(): Int

    @Query("SELECT DISTINCT slot FROM cache_chunk WHERE slot GLOB 'work-v1:*' ORDER BY slot")
    abstract suspend fun legacyWorkSlots(): List<String>

    @Query("DELETE FROM cache_chunk WHERE slot = :slot")
    abstract suspend fun delete(slot: String)

    @Insert
    abstract suspend fun insert(chunks: List<CacheChunk>)

    @Transaction
    open suspend fun replace(slot: String, chunks: List<CacheChunk>) {
        delete(slot)
        insert(chunks)
    }
}

@Database(entities = [CacheChunk::class], version = 1)
internal abstract class TaskCacheDatabase : RoomDatabase() {
    abstract fun entries(): CacheDao

    companion object {
        const val WORK_KEY_ALIAS = "ipqc.shared-task-cache.v1"

        fun openWorkspace(context: Context) = open(context, File(context.noBackupFilesDir, "patrol-task-cache.db"))

        fun open(context: Context, file: File): TaskCacheDatabase =
            Room.databaseBuilder(context.applicationContext, TaskCacheDatabase::class.java, file.absolutePath).build()
    }
}

internal interface CacheRows {
    suspend fun <T> transaction(block: suspend () -> T): T
    suspend fun read(slot: String): ByteArray?
    suspend fun count(): Int
    suspend fun legacyWorkSlots(): List<String>
    suspend fun replace(slot: String, sealed: ByteArray)
}

internal class RoomCacheRows(private val database: TaskCacheDatabase) : CacheRows {
    private val dao = database.entries()

    override suspend fun <T> transaction(block: suspend () -> T): T = database.withTransaction(block)

    override suspend fun read(slot: String): ByteArray? {
        val chunks = dao.read(slot)
        if (chunks.isEmpty()) return null
        var size = 0
        chunks.forEachIndexed { part, chunk ->
            require(chunk.part == part && chunk.ciphertext.size in 1..CHUNK_SIZE)
            require(part == chunks.lastIndex || chunk.ciphertext.size == CHUNK_SIZE)
            size = Math.addExact(size, chunk.ciphertext.size)
        }
        val result = ByteArray(size)
        var offset = 0
        for (chunk in chunks) {
            chunk.ciphertext.copyInto(result, offset)
            offset += chunk.ciphertext.size
        }
        return result
    }

    override suspend fun count() = dao.count()

    override suspend fun legacyWorkSlots() = dao.legacyWorkSlots()

    override suspend fun replace(slot: String, sealed: ByteArray) {
        // A full frozen definition may exceed Android's per-row CursorWindow limit.
        require(sealed.isNotEmpty())
        val parts = (sealed.size - 1) / CHUNK_SIZE + 1
        val chunks = List(parts) { part ->
            val start = part * CHUNK_SIZE
            CacheChunk(slot, part, sealed.copyOfRange(start, start + minOf(CHUNK_SIZE, sealed.size - start)))
        }
        dao.replace(slot, chunks)
    }

    private companion object { const val CHUNK_SIZE = 256 * 1024 }
}
