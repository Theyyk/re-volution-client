package com.revolution.client;

import net.minecraftforge.common.MinecraftForge;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.event.FMLPreInitializationEvent;

@Mod(modid = RevolutionClientMod.MOD_ID, name = RevolutionClientMod.NAME, version = RevolutionClientMod.VERSION, clientSideOnly = true, acceptableRemoteVersions = "*")
public final class RevolutionClientMod {
    public static final String MOD_ID = "revolutionclient";
    public static final String NAME = "RE:volution Client";
    public static final String VERSION = "1.0.0";

    @Mod.EventHandler
    public void preInit(FMLPreInitializationEvent event) {
        ClientConnectionConfig config = ClientConnectionConfig.load(event.getSuggestedConfigurationFile());
        MinecraftForge.EVENT_BUS.register(new MainMenuController(config));
    }
}
