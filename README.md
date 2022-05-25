# FCE_Lua_Robots
Lua Programmable Robots for FortressCraft Evolved

Lua Bots are programmable robots utilizing the
Lua programming language. All the usual features
of the language (if statements, variables,
for loops) can be used to program your robots.

This mod was created using [MoonSharp](https://www.moonsharp.org/)
A Lua interpreter written entirely in C# for the .NET, Mono and Unity platforms.

With this mod installed, you will notice a new button at the top right
corner of the screen whenever the inventory panel is open.

Clicking this button will consume 1 Constructobot Crate from your inventory
and spawn a Lua bot at your location.

Look at the robot and press E to open the robot's GUI.

The code editing text area appears on the left.
The robot's inventory, which is text based, appears on the right.

Below the code editing window is a text field for entering file names.
Enter a file name here and click the 'save' button to save the file to disk.
Enter a file name and click the 'load' button to load a program.

All files should use the .lua file extension or the mod will not recognize them.

Some example programs are included with the mod:

help.lua - displays information about the mod.
quarry.lua - the robot digs a 10x10x10 hole in the ground.
tunnel.lua - the robot digs a 50x2x2 tunnel.
wall.lua - the robot builds a 10x1x10 wall.

Once a program has been entered in the code editor, click the run button to start the program.

Click the close button to close the GUI.

Lua bots can dig, build, interact with hoppers and harvest plants from hydroponics bays.

All available lua bot commands are listed below.

--------------------------
Robot Commands
--------------------------

Move(x,y,z)
	-Moves the robot 1 meter in any direction.
	-For example, use Move(0,1,0) to move up.
	-X,Y,Z values above 1 will be reduced to 1.

Dig(x,y,z)
	-Digs the block at (x,y,z) relative to the robot.
	-For example, use Dig(0,-1,0) to dig down.
	-X,Y,Z values above 1 will be reduced to 1.

Build(x,y,z)
	-Places a block at (x,y,z) relative to the robot.
	-X,Y,Z values above 1 will be reduced to 1.

Harvest(x,y,z)
	-Harvests plants from a hydroponics bay
	 at (x,y,z) relative to the robot and adds 
	 them to the robot's inventory.
	-X,Y,Z values above 1 will be reduced to 1.

TakeFromHopper(x,y,z)
	-Removes all items from a hopper
	 at (x,y,z) relative to the robot and 
	 adds them to the robot's inventory.
	-X,Y,Z values above 1 will be reduced to 1.

EmptyToHopper(x,y,z)
	-The robot will attempt to place the
	 items it is carrying into a hopper
	 at (x,y,z) relative to the robot.
	-X,Y,Z values above 1 will be reduced to 1.
