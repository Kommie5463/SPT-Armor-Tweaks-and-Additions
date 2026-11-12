# SPT-Armor-Tweaks-and-Additions
This SPT Mod is a few tweaks and additions that I noticed would fit well within the vanilla game and alongside each other and/or other mods. 

- **Armor Stacking**: Wear body armor and an armored rig together, with soft armor taking part in the shot path.
- **Soft Armor Swap**: Remove and swap soft armor and helmet inserts, with weight and penalties that scale by how many are installed.
- **Armor Refurbishment**: Kits that restore soft armor and helmet inserts to factory condition.
- **Armor Readout**: Shows the armor points covering each zone of your body in the health tab.

# Armor Stacking
This module lets you wear body armor and an armored rig together, adding a second layer to any armored area. The SVM option for this has no effect on the module, meaning it works with or without it.

In vanilla SPT, even with SVM, a shot to the chest, back or sides hits the removable plates first and ignores the soft armor inserts until both plates break (rig plate, body armor plate, rig soft armor, body armor soft armor). By then the soft armor, usually a lower class, breaks quickly and doesn't help.

With this module the layers are ordered rig plate, rig soft armor, body armor plate, body armor soft armor. Each layer the bullet gets through lowers its penetration power using vanilla SPT's own shot calculation, so a high-penetration round can stop earlier than it otherwise would. Blunt damage also still uses vanilla behavior.

# Soft Armor Swap
This module unlocks the soft armor inserts in body armor and armored rigs, the inserts in helmets, the inserts in modded armor, and the leg armor from Manimal's leg armor mod. You can remove and swap them to raise or lower the armor class and armor points in each slot of your favorite gear.

- Penalties on body armor and rigs scale with how many soft armor slots are filled. Half the slots filled means half the penalty.
- Inserts now have weight, so gear gets heavier or lighter as you add and remove them. With every insert in, penalties and weight match the vanilla values.
- Every insert has an in-world model, so you can drop one in a raid and pick it back up, and the inventory icons look as they should.

# Armor Refurbishment
This module adds a soft armor refurbishment kit and a helmet refurbishment kit. They are meant to be used alongside Wade's armor plate restoration and Egbog's repair max durability mods. An insert must be at its maximum repairable durability before it can be refurbished, and the kit then restores it to factory condition.

Drag a kit onto a piece of gear to use it, each kit has 5 uses and gives repair skill XP, the kits are sold by Ragman at loyalty level 2 (95k for the soft armor kit, 80k for the helmet kit) and on the flea market.

# Armor Readout
This module adds a readout to the health tab of the inventory. It shows the total armor points for each zone of your body, such as chest front, back and sides, head, nape and face, and thigh and calf if you use a leg armor mod.

# Credits
I didn't use anyone else's code, but I did use Wade's Plate Restoration mod and Egbog's Repair Max Durability mod as the baseline for my refurbishment module. Thank you both. If either of you has concerns about it, please contact me. I also want to thank Manimal for the leg armor mod that the swap and readout modules support.

# Disclosure
I used Claude Sonnet 5.5 Medium to aid in my development of my mods, I read the code and test everything thoroughly in a vanilla and heavily modded environment before I feel confident in releasing it.
