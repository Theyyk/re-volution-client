package com.revolution.client;

import net.minecraftforge.common.config.Configuration;
import java.io.File;

final class ClientConnectionConfig {
    private final String host;
    private final int port;

    private ClientConnectionConfig(String host, int port) {
        this.host = host;
        this.port = port;
    }

    static ClientConnectionConfig load(File file) {
        Configuration configuration = new Configuration(file);
        try {
            configuration.load();
            String host = configuration.getString("host", "server", "", "Target server host written by RE:volution Launcher.").trim();
            int port = configuration.getInt("port", "server", 25565, 1, 65535, "Target server port written by RE:volution Launcher.");
            return new ClientConnectionConfig(host, port);
        } finally {
            if (configuration.hasChanged()) configuration.save();
        }
    }

    String host() { return host; }
    int port() { return port; }
    boolean isConfigured() { return !host.isEmpty(); }
}
