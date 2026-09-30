# Changelog

## 1.0.0

- Builds a shotgun from the blowgun already in PEAK, so the mod needs no extra files
- Roots always forces two random luggage to have a shotgun; `CanSpawnOnAnyBiome` (default false) allows rolls in other biomes
- With `EnableDebugMode` on, spawns three test zombies and a shotgun near you on the Shore. Off by default
- Each shot pushes the shooter back. `EnableRecoil` turns it on or off, `Recoil` sets the strength (0-10)
- Blasts injure scouts and flip them
- Each shotgun holds 5 shots, set by the `Shots` config entry. The hotbar name and shoot prompt show how many are left
- Knocked-out zombies stop making sounds while the game still has them alive
- Hotbar icon is a shotgun drawing
- Each shot flashes a short light at the barrel
- Shot fire sound is `shotgun_fire_01.wav`
- Blasts flip zombies harder than a coconut and add Drowsy damage
