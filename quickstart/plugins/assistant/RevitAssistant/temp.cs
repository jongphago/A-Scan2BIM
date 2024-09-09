

    public Result OnStartup(UIControlledApplication a)
    {
        // some paths relative to the addin directory
        string addin_dir = a.ControlledApplication.CurrentUserAddinsLocation;
        dll_path = Path.Combine(addin_dir, "RevitAssistant.dll");
        icons_root = Path.Combine(addin_dir, @"icons\");

        // prep the icon
        BitmapImage icon_img = new BitmapImage(new Uri(icons_root + "my_icon.png"));

        // button is inside a panel
        RibbonPanel panel = a.CreateRibbonPanel("My Panel");

        PushButton button = panel.AddItem(new PushButtonData("button",
                                          "My Button",
                                          dll_path,
                                          "RevitAssistant.MyFunction")) as PushButton;
        button.LargeImage = icon_img.Clone();

        return Result.Succeeded;
    }

    [Transaction( TransactionMode.Manual )]
    public class MyFunction : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;

            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // your routine

            return Result.Succeeded;
        }
    }


