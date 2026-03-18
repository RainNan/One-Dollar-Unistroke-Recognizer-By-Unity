## How to Use

Create a new **Panel** in **UGUI**, remove its **Image** component, and attach `UnistrokePanel.cs` to the Panel. Then run the game.

Recognition requires templates first. In the **Game** window, hold the **right mouse button** to draw a gesture. In the **Inspector**, set `currentTemplateName`, then use the custom **ContextMenu** function **Save Current Template** to save the current gesture as a template.

After that, draw again and the system will be able to recognize it. You can also add more templates. To improve confidence, it is recommended to save multiple templates for the same gesture.
