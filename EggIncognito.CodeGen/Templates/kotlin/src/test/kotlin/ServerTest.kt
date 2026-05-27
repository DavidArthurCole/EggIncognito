import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.nio.file.Path
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class ServerTest {

    companion object {
        const val EID_PROTO = "MhJFSTAwMDAwMDAwMDAwMDAwMDE="
    }

    @Test
    fun extractEidReturnsCorrectEid() {
        assertEquals("EI0000000000000001", extractEid(EID_PROTO))
    }

    @Test
    fun extractEidEmptyInput() {
        assertEquals("", extractEid(""))
    }

    @Test
    fun loadFixtureDefault(@TempDir dir: Path) {
        val defDir = dir.resolve("default").toFile().also { it.mkdirs() }
        File(defDir, "test.binpb").writeBytes(byteArrayOf(0x01, 0x02))

        val orig = FIXTURES_PATH
        FIXTURES_PATH = dir.toString()
        try {
            val data = loadFixture("test", "")
            assertTrue(data.contentEquals(byteArrayOf(0x01, 0x02)))
        } finally {
            FIXTURES_PATH = orig
        }
    }

    @Test
    fun loadFixtureEidOverride(@TempDir dir: Path) {
        File(dir.resolve("default").toString()).mkdirs()
        File(dir.resolve("eids/EI0000000000000001").toString()).mkdirs()
        File(dir.resolve("default/test.binpb").toString()).writeBytes(byteArrayOf(0x01))
        File(dir.resolve("eids/EI0000000000000001/test.binpb").toString()).writeBytes(byteArrayOf(0x02))

        val orig = FIXTURES_PATH
        FIXTURES_PATH = dir.toString()
        try {
            val data = loadFixture("test", "EI0000000000000001")
            assertTrue(data.contentEquals(byteArrayOf(0x02)))
        } finally {
            FIXTURES_PATH = orig
        }
    }

    @Test
    fun loadFixtureMissing(@TempDir dir: Path) {
        val orig = FIXTURES_PATH
        FIXTURES_PATH = dir.toString()
        try {
            val data = loadFixture("nonexistent", "")
            assertEquals(0, data.size)
        } finally {
            FIXTURES_PATH = orig
        }
    }
}
