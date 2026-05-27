package com.egginc.mock;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.io.IOException;
import java.nio.file.*;
import static org.junit.jupiter.api.Assertions.*;

class ServerTest {

    private static final String EID_PROTO = "MhJFSTAwMDAwMDAwMDAwMDAwMDE=";

    @Test
    void extractEidReturnsCorrectEid() {
        assertEquals("EI0000000000000001", Server.extractEid(EID_PROTO));
    }

    @Test
    void extractEidEmptyInput() {
        assertEquals("", Server.extractEid(""));
    }

    @Test
    void loadFixtureDefault(@TempDir Path dir) throws IOException {
        Path defDir = dir.resolve("default");
        Files.createDirectories(defDir);
        Files.write(defDir.resolve("test.binpb"), new byte[]{0x01, 0x02});

        String orig = Server.FIXTURES_PATH;
        Server.FIXTURES_PATH = dir.toString();
        try {
            byte[] data = Server.loadFixture("test", "");
            assertArrayEquals(new byte[]{0x01, 0x02}, data);
        } finally {
            Server.FIXTURES_PATH = orig;
        }
    }

    @Test
    void loadFixtureEidOverride(@TempDir Path dir) throws IOException {
        Files.createDirectories(dir.resolve("default"));
        Files.createDirectories(dir.resolve("eids/EI0000000000000001"));
        Files.write(dir.resolve("default/test.binpb"), new byte[]{0x01});
        Files.write(dir.resolve("eids/EI0000000000000001/test.binpb"), new byte[]{0x02});

        String orig = Server.FIXTURES_PATH;
        Server.FIXTURES_PATH = dir.toString();
        try {
            byte[] data = Server.loadFixture("test", "EI0000000000000001");
            assertArrayEquals(new byte[]{0x02}, data);
        } finally {
            Server.FIXTURES_PATH = orig;
        }
    }

    @Test
    void loadFixtureMissing(@TempDir Path dir) {
        String orig = Server.FIXTURES_PATH;
        Server.FIXTURES_PATH = dir.toString();
        try {
            byte[] data = Server.loadFixture("nonexistent", "");
            assertEquals(0, data.length);
        } finally {
            Server.FIXTURES_PATH = orig;
        }
    }
}
