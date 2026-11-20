package com.jaxpower.ipqc.patrol.offline

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import java.util.UUID

private const val WORKSPACE_SLOT = "workspace-v1"

// Both callers hold the same kind of Room transaction across read and bootstrap.
internal suspend fun workspaceId(rows: CacheRows, cipher: CacheCipher, create: Boolean): String? {
    val sealed = rows.read(WORKSPACE_SLOT)
    if (sealed != null) return decode(cipher.open(WORKSPACE_SLOT, sealed)) {
        UUID.fromString(it).toString().also { id -> check(id == it) }
    }
    if (rows.count() != 0) throw PatrolFailure(Issue.CACHE_STORAGE)
    if (!create) return null
    val id = UUID.randomUUID().toString()
    rows.replace(WORKSPACE_SLOT, cipher.seal(WORKSPACE_SLOT, id.encodeToByteArray(), allowNewKey = true))
    return id
}
