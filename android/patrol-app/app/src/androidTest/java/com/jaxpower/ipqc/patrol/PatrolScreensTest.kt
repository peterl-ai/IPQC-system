package com.jaxpower.ipqc.patrol

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.DeviceConfigurationOverride
import androidx.compose.ui.test.ForcedSize
import androidx.compose.ui.test.assertHasNoClickAction
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.ui.CredentialsPage
import com.jaxpower.ipqc.patrol.ui.HomePage
import com.jaxpower.ipqc.patrol.ui.PatrolUi
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PatrolScreensTest {
    @get:Rule val compose = createComposeRule()

    @Test fun homeHasOneExecutablePatrolEntryAndAnInertMaterialPlaceholder() {
        var patrolTaps = 0
        compose.setContent { MaterialTheme { HomePage("Fixture inspector") { patrolTaps++ } } }
        compose.onNodeWithText("Material Verification").assertIsDisplayed().assertHasNoClickAction()
        compose.onNodeWithText("Not available").assertIsDisplayed()
        compose.onNodeWithText("Patrol").performClick()
        compose.runOnIdle { assertEquals(1, patrolTaps) }
    }

    @Test fun patrolEntryRemainsOperableAtPortraitAndLandscapeWidths() {
        val size = mutableStateOf(DpSize(500.dp, 900.dp))
        var patrolTaps = 0
        compose.setContent {
            DeviceConfigurationOverride(DeviceConfigurationOverride.ForcedSize(size.value)) {
                MaterialTheme { HomePage("Fixture inspector") { patrolTaps++ } }
            }
        }
        val patrol = compose.onNodeWithText("Patrol")
        val material = compose.onNodeWithText("Material Verification")
        assertTrue(material.fetchSemanticsNode().boundsInRoot.top > patrol.fetchSemanticsNode().boundsInRoot.bottom)
        patrol.performClick()
        compose.runOnIdle { size.value = DpSize(1000.dp, 600.dp) }
        assertTrue(material.fetchSemanticsNode().boundsInRoot.left > patrol.fetchSemanticsNode().boundsInRoot.right)
        material.assertIsDisplayed().assertHasNoClickAction()
        patrol.performClick()
        compose.runOnIdle { assertEquals(2, patrolTaps) }
    }

    @Test fun offlineScreenBlocksSignInAndSessionResume() {
        compose.setContent {
            MaterialTheme {
                CredentialsPage(PatrolUi(online = false, issue = Issue.OFFLINE), false,
                    submit = { _, _ -> error("Offline submission") },
                    resume = { error("Offline resume") }, forget = {})
            }
        }
        compose.onNodeWithText("Sign in").assertIsNotEnabled()
        compose.onNodeWithText("Resume saved session").assertIsNotEnabled()
        compose.onNodeWithText(Issue.OFFLINE.message).assertIsDisplayed()
    }
}
