package com.revolution.client;

import net.minecraft.client.gui.GuiMainMenu;
import net.minecraftforge.client.event.GuiOpenEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;

final class MainMenuController {
    private final ClientConnectionConfig config;

    MainMenuController(ClientConnectionConfig config) { this.config = config; }

    @SubscribeEvent
    public void onGuiOpen(GuiOpenEvent event) {
        if (event.getGui() instanceof GuiMainMenu && !(event.getGui() instanceof RevolutionMainMenu)) {
            event.setGui(new RevolutionMainMenu(config));
        }
    }
}
