package com.revolution.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiButton;
import net.minecraft.client.gui.GuiConnecting;
import net.minecraft.client.gui.GuiScreen;
import java.io.IOException;

final class RevolutionMainMenu extends GuiScreen {
    private static final int PLAY_BUTTON = 1;
    private final ClientConnectionConfig config;

    RevolutionMainMenu(ClientConnectionConfig config) { this.config = config; }

    @Override
    public void initGui() {
        buttonList.clear();
        int width = 220;
        int x = (this.width - width) / 2;
        int y = this.height / 2;
        GuiButton play = new GuiButton(PLAY_BUTTON, x, y, width, 20, "Играть");
        play.enabled = config.isConfigured();
        buttonList.add(play);
    }

    @Override
    protected void actionPerformed(GuiButton button) throws IOException {
        if (button.id == PLAY_BUTTON && config.isConfigured()) {
            Minecraft minecraft = Minecraft.getMinecraft();
            minecraft.displayGuiScreen(new GuiConnecting(this, minecraft, config.host(), config.port()));
        }
    }

    @Override
    public void drawScreen(int mouseX, int mouseY, float partialTicks) {
        drawDefaultBackground();
        drawCenteredString(fontRenderer, "RE:volution", width / 2, height / 2 - 50, 0xFFFFFF);
        if (!config.isConfigured())
            drawCenteredString(fontRenderer, "Сервер не настроен лаунчером", width / 2, height / 2 + 30, 0xFF7777);
        super.drawScreen(mouseX, mouseY, partialTicks);
    }
}
