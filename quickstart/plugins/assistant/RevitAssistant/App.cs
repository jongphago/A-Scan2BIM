#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using System.Windows.Media.Imaging;
#endregion // Namespaces

namespace RevitAssistant
{

  public class Step
  {
    public string id;
    public List<double> coords;
    public string action;
  }

  public class ModifyContainer
  {
    public Dictionary<int, List<double>> walls;
    public List<int> wall_order;

    public int selected_id;
    public List<int> modified_ids;
    public List<List<List<double>>> suggestions;
    public List<List<double>> bad_suggestions;

    // stage 1 labels
    public List<string> correct;
    public List<string> modify;
    public List<string> delete;

    // for stage 1
    public int last_action_type;
    public int edited_wall_id;
    public List<double> new_location;

    // for newly added corners
    public List<XYZ> new_corners;

    public IEnumerable<Step> steps;
  }

  public class Sequence
  {
    public List<List<double>> coords;
    public List<ElementId> eids;
  }

  public class WallWarningSwallower : IFailuresPreprocessor
  {
    public FailureProcessingResult PreprocessFailures(
      FailuresAccessor a )
    {
      // inside event handler, get all warnings

      IList<FailureMessageAccessor> failures
        = a.GetFailureMessages();

      foreach( FailureMessageAccessor f in failures )
        a.DeleteWarning( f );

      return FailureProcessingResult.Continue;
    }
  }

  class App : IExternalApplication
  {
    /// <summary>
    /// Singleton external application class instance.
    /// </summary>
    internal static App _app = null;

    // message types
    public byte[] MSG_INIT_HEAT = BitConverter.GetBytes(0);
    public byte[] MSG_MODIFY = BitConverter.GetBytes(1);
    public byte[] MSG_SUGGEST = BitConverter.GetBytes(2);
    public byte[] MSG_INIT_GT = BitConverter.GetBytes(3);

    // colors
    Color YELLOW = new Color(246, 190, 0);
    Color GREEN = new Color(0, 255, 0);
    Color RED = new Color(255, 0, 0);
    Color PINK = new Color(255, 175, 255);

    // line styles
    Category yellow_ls_cat = null;
    Category green_ls_cat = null;
    Category red_ls_cat = null;
    Category pink_1_ls_cat = null;
    Category pink_2_ls_cat = null;
    Category pink_3_ls_cat = null;

    // wall widths
    public List<WallType> wall_types;

    Category corner_ls_cat;

    public bool mode_select = false;
    public bool autocomplete = false;
    public bool full_auto = false;
    public bool need_suggestion = false;
    List<int> wall_order = new List<int>();
    List<ElementId> suggest_walls = new List<ElementId>();
    List<Sequence> suggest_seqs = new List<Sequence>();
    List<List<List<double>>> new_suggestions = new List<List<List<double>>>();
    List<List<double>> bad_suggestions = new List<List<double>>();

    // so that we know which walls to send
    List<ElementId> wall_ids;
    public Dictionary<int, List<Double>> wall_db;

    // stores new corners that the user added
    List<ElementId> new_corner_eids;

    // for GT visualization
    List<ElementId> gt_wall_ids;
    bool gt_visible;

    // for GT rollout visualization
    PushButton btn_forward;
    public List<Step> steps;
    public int curr_step;
    string dll_path = @"RevitAssistant.dll";
    string icons_root = @"icons\";

    // buttons for stuff :P
    PushButton btn_obtain_prediction;
    PushButton btn_add_corner;
    PushButton btn_send_new_corners;
    PushButton btn_toggle_autocomplete;
    PushButton btn_accept_suggestion;
    PushButton btn_accept_green;
    PushButton btn_accept_yellow;
    PushButton btn_accept_red;
    PushButton btn_auto;
    PushButton btn_save_transform;

    /// <summary>
    /// Provide access to singleton class instance.
    /// </summary>
    public static App Instance
    {
      get { return _app; }
    }

    public Result OnStartup(UIControlledApplication a)
    {
      _app = this;

      string addin_dir = a.ControlledApplication.CurrentUserAddinsLocation;
      dll_path = Path.Combine(addin_dir, "RevitAssistant.dll");
      icons_root = Path.Combine(addin_dir, @"icons\");

      // all the images needed
      BitmapImage debug_img = new BitmapImage(new Uri(icons_root + "debug.png"));
      BitmapImage step_img = new BitmapImage(new Uri(icons_root + "next_step.png"));
      BitmapImage save_img = new BitmapImage(new Uri(icons_root + "save.png"));

      // button to handle GT rollout
      RibbonPanel panel = a.CreateRibbonPanel("Auto Revit");

      btn_obtain_prediction = panel.AddItem(new PushButtonData("btn_obtain_prediction",
                                            "Obtain Prediction",
                                            dll_path,
                                            "RevitAssistant.ObtainPrediction")) as PushButton;
      btn_obtain_prediction.LargeImage = debug_img.Clone();

      btn_add_corner = panel.AddItem(new PushButtonData("btn_add_corner",
                                            "Add Corner",
                                            dll_path,
                                            "RevitAssistant.AddCorner")) as PushButton;
      btn_add_corner.LargeImage = debug_img.Clone();

      btn_send_new_corners = panel.AddItem(new PushButtonData("btn_send_new_corners",
                                            "Send Corners",
                                            dll_path,
                                            "RevitAssistant.SendNewCorners")) as PushButton;
      btn_send_new_corners.LargeImage = debug_img.Clone();

      btn_toggle_autocomplete = panel.AddItem(new PushButtonData("btn_toggle_autocomplete",
                                            "Autocomplete",
                                            dll_path,
                                            "RevitAssistant.ToggleAutocomplete")) as PushButton;
      btn_toggle_autocomplete.LargeImage = step_img.Clone();

      btn_accept_suggestion = panel.AddItem(new PushButtonData("btn_accept_suggestion",
                                            "Accept",
                                            dll_path,
                                            "RevitAssistant.AcceptSuggestion")) as PushButton;
      btn_accept_suggestion.LargeImage = step_img.Clone();

      // the three colors
      btn_accept_green = panel.AddItem(new PushButtonData("btn_accept_green",
                                            "Accept Green",
                                            dll_path,
                                            "RevitAssistant.AcceptSuggestionGreen")) as PushButton;
      btn_accept_green.LargeImage = step_img.Clone();

      btn_accept_yellow = panel.AddItem(new PushButtonData("btn_accept_yellow",
                                            "Accept Yellow",
                                            dll_path,
                                            "RevitAssistant.AcceptSuggestionYellow")) as PushButton;
      btn_accept_yellow.LargeImage = step_img.Clone();

      btn_accept_red = panel.AddItem(new PushButtonData("btn_accept_red",
                                            "Accept Red",
                                            dll_path,
                                            "RevitAssistant.AcceptSuggestionRed")) as PushButton;
      btn_accept_red.LargeImage = step_img.Clone();

      // full auto mode
      btn_auto = panel.AddItem(new PushButtonData("btn_auto",
                                            "Full Auto",
                                            dll_path,
                                            "RevitAssistant.FullAuto")) as PushButton;
      btn_auto.LargeImage = step_img.Clone();

      // save transforms
      btn_save_transform = panel.AddItem(new PushButtonData("btn_save_transform",
                                         "Save Transform",
                                         dll_path,
                                         "RevitAssistant.SaveTransform")) as PushButton;
      btn_save_transform.LargeImage = step_img.Clone();

      // event listener will handle all recording
      a.ControlledApplication.DocumentChanged
          += new EventHandler<DocumentChangedEventArgs>(
          OnDocumentChanged);

      a.ControlledApplication.DocumentOpened
          += new EventHandler<DocumentOpenedEventArgs>(
          OnDocumentOpened);

      return Result.Succeeded;
    }

    static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
    {
      Document doc = e.Document;
      Instance.wall_ids = new List<ElementId>();
      Instance.wall_db = new Dictionary<int, List<double>>();
      Instance.UpdateDB(doc);

      Instance.gt_wall_ids = new List<ElementId>();
      Instance.gt_visible = false;

      Instance.new_corner_eids = new List<ElementId>();

      Instance.curr_step = 0;
      Instance.steps = new List<Step>();

      Instance.bad_suggestions = new List<List<double>>();

      // create the necessary linestyles if needed
      CreateLineStyles(doc);
      CreateWallTypes(doc);
    }

    static void CreateLineStyles(Document doc)
    {
      // get the "Dash" line pattern
      FilteredElementCollector fec
        = new FilteredElementCollector( doc )
          .OfClass( typeof( LinePatternElement ) );

      LinePatternElement linePatternElem_dash_dense = fec
        .Cast<LinePatternElement>()
        .First<LinePatternElement>( linePattern
          => linePattern.Name == "Chain Dash" );

      LinePatternElement linePatternElem_dash_sparse = fec
        .Cast<LinePatternElement>()
        .First<LinePatternElement>( linePattern
          => linePattern.Name == "Chain Dash 2" );

      // we need two line styles, one for the new corner and another for the suggestions
      Categories categories = doc.Settings.Categories;
      Category line_cat = categories.get_Item(BuiltInCategory.OST_Lines);

      Instance.red_ls_cat = null;
      Instance.green_ls_cat = null;
      Instance.yellow_ls_cat = null;
      Instance.pink_1_ls_cat = null;
      Instance.pink_2_ls_cat = null;
      Instance.pink_3_ls_cat = null;
      Instance.corner_ls_cat = null;

      foreach (Category line_style in line_cat.SubCategories)
      {
        // multiple choice lines
        if (line_style.Name == "Suggest Lines Red")
          Instance.red_ls_cat = line_style;
        if (line_style.Name == "Suggest Lines Yellow")
          Instance.yellow_ls_cat = line_style;
        if (line_style.Name == "Suggest Lines Green")
          Instance.green_ls_cat = line_style;

        // sequence lines
        if (line_style.Name == "Suggest Lines Pink 1")
          Instance.pink_1_ls_cat = line_style;
        if (line_style.Name == "Suggest Lines Pink 2")
          Instance.pink_2_ls_cat = line_style;
        if (line_style.Name == "Suggest Lines Pink 3")
          Instance.pink_3_ls_cat = line_style;

        if (line_style.Name == "Corner Lines")
          Instance.corner_ls_cat = line_style;
      }

      using (Transaction t = new Transaction(doc))
      {
        t.Start("Create LineStyle");

        if (Instance.red_ls_cat == null)
          Instance.red_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Red");
        if (Instance.yellow_ls_cat == null)
          Instance.yellow_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Yellow");
        if (Instance.green_ls_cat == null)
          Instance.green_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Green");

        if (Instance.pink_1_ls_cat == null)
          Instance.pink_1_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Pink 1");
        if (Instance.pink_2_ls_cat == null)
          Instance.pink_2_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Pink 2");
        if (Instance.pink_3_ls_cat == null)
          Instance.pink_3_ls_cat = categories.NewSubcategory(line_cat, "Suggest Lines Pink 3");

        if (Instance.corner_ls_cat == null)
          Instance.corner_ls_cat = categories.NewSubcategory(line_cat, "Corner Lines");

        doc.Regenerate();

        Instance.red_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.red_ls_cat.LineColor = Instance.RED;

        Instance.yellow_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.yellow_ls_cat.LineColor = Instance.YELLOW;

        Instance.green_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.green_ls_cat.LineColor = Instance.GREEN;

        Instance.pink_1_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.pink_1_ls_cat.LineColor = Instance.PINK;

        Instance.pink_2_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.pink_2_ls_cat.LineColor = Instance.PINK;
        Instance.pink_2_ls_cat.SetLinePatternId(linePatternElem_dash_dense.Id, GraphicsStyleType.Projection);

        Instance.pink_3_ls_cat.SetLineWeight(7, GraphicsStyleType.Projection);
        Instance.pink_3_ls_cat.LineColor = Instance.PINK;
        Instance.pink_3_ls_cat.SetLinePatternId(linePatternElem_dash_sparse.Id, GraphicsStyleType.Projection);

        Instance.corner_ls_cat.SetLineWeight(6, GraphicsStyleType.Projection);
        Instance.corner_ls_cat.LineColor = Instance.RED;

        t.Commit();
      }
    }

    static void CreateWallTypes(Document doc)
    {
      Instance.wall_types = new List<WallType>();
      WallType src_type = doc.GetElement(new ElementId(252)) as WallType;

      using (Transaction t = new Transaction(doc))
      {
        t.Start("Create LineStyle");

        for (double inches = 1; inches <= 48; inches++)
        {
          string type_name = String.Format("Generic Auto 2 - {0}", inches);

          // see if the wall type exists already
          FilteredElementCollector fec
            = new FilteredElementCollector( doc )
              .OfClass( typeof( WallType ) );

          WallType new_type = fec
            .Cast<WallType>()
            .First<WallType>( linePattern => linePattern.Name == type_name );

          if (new_type == null)
          {
            new_type = src_type.Duplicate(type_name) as WallType;

            // set the width in feet
            CompoundStructure cs = new_type.GetCompoundStructure();
            cs.SetLayerWidth(0, inches / 12);
            new_type.SetCompoundStructure(cs);
          }

          Instance.wall_types.Add(new_type);
        }

        t.Commit();
      }
    }

    static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
      // print out this transaction's information
      Debug.Print(e.GetTransactionNames()[0]);
      Debug.Print(e.Operation.ToString());

      // keep track of new corners, if one is deleted
      foreach (ElementId id in e.GetDeletedElementIds())
        if (Instance.new_corner_eids.Contains(id))
          Instance.new_corner_eids.Remove(id);

      // keep track of new and deleted edges, so we have an order to them
      UIApplication uiapp = sender as UIApplication;
      Document doc = e.GetDocument();

      if (!Instance.autocomplete)
        return;

      // List<ElementId> added_ids = new List<ElementId>();
      // List<ElementId> modified_ids = new List<ElementId>();
      // List<ElementId> deleted_ids = new List<ElementId>();

      int num_edits = 0;

      foreach (ElementId id in e.GetModifiedElementIds())
      {
        Wall wall = doc.GetElement(id) as Wall;
        if (wall != null)
        {
          while (Instance.wall_order.Contains(id.IntegerValue))
            Instance.wall_order.Remove(id.IntegerValue);
          Instance.wall_order.Add(id.IntegerValue);
          num_edits++;
        }
      }

      foreach (ElementId id in e.GetAddedElementIds())
      {
        Wall wall = doc.GetElement(id) as Wall;
        if (wall != null)
        {
          while (Instance.wall_order.Contains(id.IntegerValue))
            Instance.wall_order.Remove(id.IntegerValue);
          Instance.wall_order.Add(id.IntegerValue);
          num_edits++;
        }
      }

      foreach (ElementId id in e.GetDeletedElementIds())
      {
        if (Instance.wall_order.Contains(id.IntegerValue))
        {
          Debug.Print("Removed wall from order");
          while (Instance.wall_order.Contains(id.IntegerValue))
            Instance.wall_order.Remove(id.IntegerValue);
        }
        else if (e.Operation != UndoOperation.TransactionUndone)
        {
          // check if we delete a suggest line
          Sequence seq = Instance.suggest_seqs[0];

          int suggest_i = seq.eids.IndexOf(id);
          if (suggest_i != -1)
          {
            Instance.bad_suggestions.Add(seq.coords[suggest_i]);
            num_edits++;
          }
        }
      }

      if (Instance.autocomplete && (e.Operation != UndoOperation.TransactionRolledBack) & (num_edits > 0))
      {
        Instance.need_suggestion = true;
        Instance.mode_select = false;
      }
    }

    public double MaxDiff(List<double> old_coords, List<double> new_coords)
    {
      Vector ac = new Vector(new_coords[0] - old_coords[0], new_coords[1] - old_coords[1]);
      Vector bd = new Vector(new_coords[2] - old_coords[2], new_coords[3] - old_coords[3]);

      return Math.Max(ac.Length, bd.Length);
    }

    public Result OnShutdown( UIControlledApplication a )
    {
      return Result.Succeeded;
    }

    public void UpdateDB(Document doc)
    {
      FilteredElementCollector wall_collector = new FilteredElementCollector(doc).OfClass(typeof(Wall));
      IList<Element> wall_elements = wall_collector.ToElements();
      ModifyContainer container = new ModifyContainer();
      container.walls = new Dictionary<int, List<double>>();

      foreach (Element element in wall_elements)
      {
        Wall wall = element as Wall;
        if (wall.CurtainGrid == null)
        {
          int eid = wall.Id.IntegerValue;
          LocationCurve line = wall.Location as LocationCurve;
          XYZ start = line.Curve.GetEndPoint(0);
          XYZ end = line.Curve.GetEndPoint(1);
          List<double> coords = new List<double> { start.X, start.Y, end.X, end.Y };
          wall_db[eid] = coords;
        }
      }
    }

    public ModifyContainer GetWalls(Document doc)
    {
      FilteredElementCollector wall_collector = new FilteredElementCollector(doc).OfClass(typeof(Wall));
      IList<Element> wall_elements = wall_collector.ToElements();
      ModifyContainer container = new ModifyContainer();
      container.walls = new Dictionary<int, List<double>>();

      foreach (Element element in wall_elements)
      {
        Wall wall = element as Wall;

        // skip GT walls
        if (gt_wall_ids.Contains(wall.Id))
          continue;

        if (wall.CurtainGrid == null)
        {
          int eid = wall.Id.IntegerValue;
          LocationCurve line = wall.Location as LocationCurve;
          XYZ start = line.Curve.GetEndPoint(0);
          XYZ end = line.Curve.GetEndPoint(1);
          List<double> coords = new List<double> { start.X, start.Y, end.X, end.Y };
          container.walls[eid] = coords;
        }
      }

      return container;
    }

    /// <summary>
    /// Return currently active UIView or null.
    /// </summary>
    public static UIView GetActiveUiView(
      UIDocument uidoc )
    {
      Document doc = uidoc.Document;
      View view = doc.ActiveView;
      IList<UIView> uiviews = uidoc.GetOpenUIViews();
      UIView uiview = null;

      foreach( UIView uv in uiviews )
      {
        if( uv.ViewId.Equals( view.Id ) )
        {
          uiview = uv;
          break;
        }
      }
      return uiview;
    }

    /// <summary>
    /// Return the 3D view named "{3D}".
    /// </summary>
    View3D GetView3d( Document doc )
    {
      return new FilteredElementCollector( doc )
        .OfClass( typeof( View3D ) )
        .Cast<View3D>()
        .FirstOrDefault<View3D>(
          v => v.Name.Equals( "{3D}" ) );
    }

    public void IdlingHandler(object sender, IdlingEventArgs args)
    {
      UIApplication uiapp = sender as UIApplication;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;
      UIView uiview = GetActiveUiView( uidoc );

      if (need_suggestion)
      {
        // collect the current set of walls
        ModifyContainer modify_container = GetWalls(doc);

        // for wall order, double-check to remove any id that isn't in the current state
        foreach (int eid in wall_order)
          if (!modify_container.walls.ContainsKey(eid))
            wall_order.Remove(eid);
        modify_container.wall_order = wall_order;

        // also include suggestions that the user deleted
        modify_container.bad_suggestions = bad_suggestions;

        string send_str = JsonConvert.SerializeObject(modify_container);
        string resp_str = SendMessage(send_str, MSG_MODIFY);
        modify_container = JsonConvert.DeserializeObject<ModifyContainer>(resp_str);

        // temporarily save the suggestions
        if (modify_container != null)
          new_suggestions = modify_container.suggestions;
        else
          TaskDialog.Show("Error", "Backend error getting suggestions");
        need_suggestion = false;
      }

      if ((new_suggestions.Count == 0) || mode_select)
        return;

      try
      {
        // draw the suggestions
        ClearSuggestions(doc);
        SaveSuggestions(new_suggestions);
        DrawFirstSuggestion(doc, view);

        // if (full_auto)
        // {
        //   DrawSuggestedWall(doc, suggest_seqs[0].coords[0]);
        //   need_suggestion = true;
        // }
        // else
        //   DrawFirstSuggestion(doc, view);

        // clear the suggestion container
        new_suggestions.Clear();
      }
      catch (Exception e)
      {
        TaskDialog.Show("Error", "Error during suggestion drawing");
      }
    }

    public XYZ GetMouseLoc(Document doc, View view, UIView uiview)
    {
      Rectangle rect = uiview.GetWindowRectangle();

      Point p = System.Windows.Forms.Cursor.Position;

      double dx = (double) ( p.X - rect.Left )
        / ( rect.Right - rect.Left );

      double dy = (double) ( p.Y - rect.Bottom )
        / ( rect.Top - rect.Bottom );

      IList<XYZ> corners = uiview.GetZoomCorners();
      XYZ a = corners[0];
      XYZ b = corners[1];
      XYZ v = b - a;

      XYZ q = a + dx * v.X * XYZ.BasisX + dy * v.Y * XYZ.BasisY;

      // If the current view happens to be a 3D view,
      // we could simply use it right away. In
      // general we have to find a different one to
      // run the ReferenceIntersector in.

      View3D view3d = GetView3d( doc );

      XYZ viewdir = view.ViewDirection;

      XYZ origin = q + 1000 * viewdir;

      return q;
    }

    public string SendMessage(string send_str, byte[] msg_type)
    {
      try
      {
        IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
        IPEndPoint remoteEP = new IPEndPoint(ipAddress, 33333);
        Debug.Print(ipAddress.ToString());

        Socket sender = new Socket(ipAddress.AddressFamily,
                                   SocketType.Stream, ProtocolType.Tcp);

        try
        {
          sender.Connect(remoteEP);
          Debug.WriteLine("Connected to server");

          // first send the message type
          sender.Send(msg_type);

          // then send the length of the message
          byte[] send_byte = System.Text.Encoding.ASCII.GetBytes(send_str);
          byte[] msg_len = BitConverter.GetBytes(send_byte.Length);
          int bytes_sent = sender.Send(msg_len);

          // then send the message
          sender.Send(send_byte);

          // first receive the length of the response
          sender.Receive(msg_len);
          int resp_len = BitConverter.ToInt32(msg_len, 0);
          Debug.Print("Receive size: {0}", resp_len);

          byte[] resp_byte = new byte[resp_len];
          sender.Receive(resp_byte);
          string resp_str = System.Text.UTF8Encoding.Default.GetString(resp_byte);

          sender.Shutdown(SocketShutdown.Both);
          sender.Close();

          return resp_str;

        } catch (ArgumentNullException ane) {
          Debug.WriteLine("ArgumentNullException : {0}",ane.ToString());
        } catch (SocketException se) {
          Debug.WriteLine("SocketException : {0}",se.ToString());
        } catch (Exception e) {
          Debug.WriteLine("Unexpected exception : {0}", e.ToString());
        }

      } catch (Exception e) {
        Debug.WriteLine(e.ToString());
      }

      return null;
    }

    public void ObtainPrediction(Document doc, View view)
    {
      Debug.Print(view.Name);
      Debug.Print("Obtaining predictions");

      string resp_str = SendMessage("hai", MSG_INIT_HEAT);
      List<List<double>> edges = JsonConvert.DeserializeObject<List<List<double>>>(resp_str);
      Debug.Print("Number of edges: {0}", edges.Count);
      DrawHEATWalls(doc, edges);
    }

    public void AddCorners(Document doc, View view)
    {
      Debug.Print(view.Name);
      Debug.Print("Modifications");
    }

    public void SendNewCorners(Document doc, View view, UIDocument uidoc)
    {
      Debug.Print(view.Name);
      Debug.Print("Sending new corners");

      if (Instance.new_corner_eids.Count == 0)
      {
        TaskDialog.Show("Error", "Need to add new corners!");
        return;
      }
      Debug.Print("{0} new corners", Instance.new_corner_eids.Count);

      List<XYZ> new_corner_locs = new List<XYZ>();
      foreach (ElementId eid in Instance.new_corner_eids)
      {
        ModelArc arc = doc.GetElement(eid) as ModelArc;
        Arc geom_arc = arc.GeometryCurve as Arc;
        new_corner_locs.Add(geom_arc.Center);
      }

      ModifyContainer container = GetWalls(doc);
      container.new_corners = new_corner_locs;

      string send_str = JsonConvert.SerializeObject(container);
      string resp_str = SendMessage(send_str, MSG_SUGGEST);
      if (resp_str != null)
        container = JsonConvert.DeserializeObject<ModifyContainer>(resp_str);

      if ((resp_str == null) | (container == null))
      {
        // not do anything, cause of a backend error
        TaskDialog.Show("Error", "Backend error!");
      }
      else if (container.suggestions.Count() == 0)
      {
        TaskDialog.Show("Suggestions", "There are no suggestions!");
      }
      else
      {
        DrawNewWalls(doc, container.suggestions[0]);
      }

      // delete all the corners that the user added
      using (Transaction transaction = new Transaction(doc))
      {
        if (transaction.Start("Delete markers") == TransactionStatus.Started)
        {
          foreach (ElementId eid in Instance.new_corner_eids)
            doc.Delete(eid);

          if (TransactionStatus.Committed != transaction.Commit())
          {
              TaskDialog.Show("Failure", "Transaction could not be committed");
          }
        }
      }

      Instance.new_corner_eids.Clear();
    }

    public void FollowSuggestion(Document doc)
    {
      // some checks
      if (suggest_seqs.Count == 0)
      {
        TaskDialog.Show("Error", "No suggestions currently");
        return;
      }

      // we draw one wall from the top suggestion
      Sequence seq = suggest_seqs[0];
      ModelLine suggestion = doc.GetElement(seq.eids[0]) as ModelLine;
      double width = seq.coords[0][4];

      Line geom_line = suggestion.GeometryCurve as Line;
      XYZ start = geom_line.GetEndPoint(0);
      XYZ end = geom_line.GetEndPoint(1);

      List<double> new_wall = new List<double> { start.X, start.Y, end.X, end.Y, width };
      ClearSuggestions(doc);
      DrawSuggestedWall(doc, new_wall);
    }

    public void DeclineSuggestion(Document doc, View view, UIDocument uidoc)
    {
      // some checks
      if (suggest_seqs.Count == 0)
      {
        TaskDialog.Show("Error", "No suggestions currently");
        return;
      }

      // clear the suggested lines, then draw the new walls one-by-one
      ClearSuggestions(doc);
      DrawAllSuggestions(doc, view);
      mode_select = true;
    }

    public void AcceptSuggestion(Document doc, View view, UIDocument uidoc)
    {
      // get selected
      Selection selection = uidoc.Selection;
      ICollection<ElementId> selected_ids = selection.GetElementIds();

      // some checks
      if (selected_ids.Count == 0)
      {
        TaskDialog.Show("Error", "Need to select at least one suggested line");
        return;
      }

      foreach (ElementId eid in selected_ids)
      {
        ModelLine suggestion = doc.GetElement(eid) as ModelLine;

        if ((suggestion == null) || (!suggest_walls.Contains(suggestion.Id)))
        {
          TaskDialog.Show("Error", "Highlighted element is not a suggested line");
        }
        else
        {
          // add the suggested wall
          Line geom_line = suggestion.GeometryCurve as Line;
          XYZ start = geom_line.GetEndPoint(0);
          XYZ end = geom_line.GetEndPoint(1);

          List<double> new_wall = new List<double> { start.X, start.Y, end.X, end.Y };
          DrawSuggestedWall(doc, new_wall);
        }
      }
    }

    public void AcceptSuggestion(Document doc, View view, UIDocument uidoc, int seq_i)
    {
      // some checks
      if (suggest_seqs.Count == 0)
      {
        TaskDialog.Show("Error", "No suggestions currently");
        return;
      }

      // we cache the coordinates
      Sequence seq = suggest_seqs[seq_i];
      List<List<double>> new_walls = new List<List<double>>();

      for (int i = 0; i < seq.eids.Count; i++)
      {
        ElementId suggestion_eid = seq.eids[i];
        ModelLine suggestion = doc.GetElement(suggestion_eid) as ModelLine;
        double width = seq.coords[i][4];

        // add the suggested wall
        Line geom_line = suggestion.GeometryCurve as Line;
        XYZ start = geom_line.GetEndPoint(0);
        XYZ end = geom_line.GetEndPoint(1);

        List<double> new_wall = new List<double> { start.X, start.Y, end.X, end.Y, width };
        new_walls.Add(new_wall);
      }

      // clear the suggested lines, then draw the new walls one-by-one
      ClearSuggestions(doc);

      foreach (List<double> new_wall in new_walls)
        DrawSuggestedWall(doc, new_wall);
    }

    public void ToggleAutocomplete(UIApplication uiapp)
    {
      if (Instance.autocomplete)
      {
        Instance.autocomplete = false;
        uiapp.Idling -= Instance.IdlingHandler;
        btn_toggle_autocomplete.LargeImage = new BitmapImage(new Uri(icons_root + "next_step.png"));
      }
      else
      {
        Instance.autocomplete = true;
        uiapp.Idling += new EventHandler<IdlingEventArgs>(Instance.IdlingHandler);
        btn_toggle_autocomplete.LargeImage = new BitmapImage(new Uri(icons_root + "paused.png"));
      }
    }

    public void DrawHEATWalls(Document doc, List<List<double>> edges)
    {
      Instance.autocomplete = false;

      Level level = doc.GetElement(new ElementId(30)) as Level;
      double height = 7;
      double offset = 0;

      foreach (List<double> edge in edges)
      {
        using (Transaction transaction = new Transaction(doc))
        {
          transaction.Start("Drawing walls");

          int width = (int)edge[4];
          WallType wall_type = wall_types[width];

          XYZ start = new XYZ(edge[0], edge[1], 0);
          XYZ end = new XYZ(edge[2], edge[3], 0);
          Line line = Line.CreateBound(start, end);
          Wall wall = Wall.Create(doc, line, wall_type.Id, level.Id, height, offset, true, true);
          wall_ids.Add(wall.Id);

          transaction.Commit();
        }
      }

      Instance.autocomplete = true;
    }

    public void DrawNewWalls(Document doc, List<List<double>> edges)
    {
      // Instance.autocomplete = false;

      WallType wall_type = doc.GetElement(new ElementId(252)) as WallType;
      Level level = doc.GetElement(new ElementId(30)) as Level;
      double height = 7;
      double offset = 0;

      using (Transaction transaction = new Transaction(doc))
      {
        transaction.Start("Drawing walls");

        foreach (List<double> edge in edges)
        {
          XYZ start = new XYZ(edge[0], edge[1], 0);
          XYZ end = new XYZ(edge[2], edge[3], 0);
          Line line = Line.CreateBound(start, end);
          Wall wall = Wall.Create(doc, line, wall_type.Id, level.Id, height, offset, true, true);
          wall_ids.Add(wall.Id);

          OverrideGraphicSettings ogs = new OverrideGraphicSettings();
          ogs.SetProjectionLineColor(GREEN);
          ogs.SetProjectionLineWeight(4);

          doc.ActiveView.SetElementOverrides(wall.Id, ogs);
        }

        transaction.Commit();
      }

      // Instance.autocomplete = true;
    }

    public void DrawSuggestedWall(Document doc, List<double> edge)
    {
      Level level = doc.GetElement(new ElementId(30)) as Level;
      double height = 7;
      double offset = 0;

      using (Transaction transaction = new Transaction(doc))
      {
        transaction.Start("Drawing walls");

        // FailureHandlingOptions fail_opt = transaction.GetFailureHandlingOptions();
        // fail_opt.SetFailuresPreprocessor(new WallWarningSwallower());
        // transaction.SetFailureHandlingOptions(fail_opt);

        int width = (int)edge[4];
        WallType wall_type = wall_types[width];

        XYZ start = new XYZ(edge[0], edge[1], 0);
        XYZ end = new XYZ(edge[2], edge[3], 0);
        Line line = Line.CreateBound(start, end);
        Wall wall = Wall.Create(doc, line, wall_type.Id, level.Id, height, offset, true, true);
        wall_ids.Add(wall.Id);

        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(GREEN);
        ogs.SetProjectionLineWeight(4);

        doc.ActiveView.SetElementOverrides(wall.Id, ogs);

        transaction.Commit();
      }
    }

    public void DrawSequence(Document doc, Sequence sequence)
    {
      int seq_count = sequence.coords.Count;

      // we might be redrawing the sequence, so clear it first
      sequence.eids.Clear();

      for (int coord_i = 0; coord_i < seq_count; coord_i++)
      {
        List<double> xyxy = sequence.coords[coord_i];

        int z = 100;
        if (coord_i == 0)
          z = 150;

        // Create a geometry plane in Revit application
        XYZ normal = new XYZ(0, 0, 1);
        XYZ origin = new XYZ(0, 0, z);
        Plane geomPlane = Plane.CreateByNormalAndOrigin(normal, origin);

        // Create a sketch plane in current document
        SketchPlane sketch = SketchPlane.Create(doc, geomPlane);

        XYZ start = new XYZ(xyxy[0], xyxy[1], z);
        XYZ end = new XYZ(xyxy[2], xyxy[3], z);
        Line geom_line = Line.CreateBound(start, end);

        // Create a ModelArc element using the created geometry arc and sketch plane
        ModelLine line = doc.Create.NewModelCurve(geom_line, sketch) as ModelLine;

        // change its color and weight
        if (coord_i == 0)
          line.LineStyle = pink_1_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);
        else if (coord_i == 1)
          line.LineStyle = pink_2_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);
        else if (coord_i == 2)
          line.LineStyle = pink_3_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);

        sequence.eids.Add(line.Id);
      }
    }

    public void ToggleFullAuto(UIApplication uiapp)
    {
      if (!full_auto)
      {
        full_auto = true;
        need_suggestion = true;
        mode_select = false;
        uiapp.Idling += new EventHandler<IdlingEventArgs>(Instance.IdlingHandler);
        btn_auto.LargeImage = new BitmapImage(new Uri(icons_root + "paused.png"));
      }
      else
      {
        full_auto = false;
        need_suggestion = false;
        mode_select = false;
        uiapp.Idling -= new EventHandler<IdlingEventArgs>(Instance.IdlingHandler);
        btn_auto.LargeImage = new BitmapImage(new Uri(icons_root + "next_step.png"));
      }
    }

    public void ClearSuggestions(Document doc)
    {
      autocomplete = false;

      using (Transaction transaction = new Transaction(doc))
      {
        if (transaction.Start("Clear sequence") == TransactionStatus.Started)
        {
          foreach (Sequence seq in suggest_seqs)
          {
            foreach (ElementId eid in seq.eids)
            {
              Element e = doc.GetElement(eid);
              if (e != null)
                doc.Delete(eid);
            }
          }

          if (TransactionStatus.Committed != transaction.Commit())
          {
              TaskDialog.Show("Failure", "Transaction could not be committed");
          }
        }
      }

      autocomplete = true;
    }

    public void SaveSuggestions(List<List<List<double>>> suggestions)
    {
      suggest_seqs.Clear();

      // we should have three sequences
      for (int seq_i = 0; seq_i < suggestions.Count; seq_i++)
      {
        Sequence sequence = new Sequence();
        sequence.coords = suggestions[seq_i];
        sequence.eids = new List<ElementId>();

        suggest_seqs.Add(sequence);
      }
    }

    public void DrawFirstSuggestion(Document doc, View view)
    {
      Instance.autocomplete = false;

      using (Transaction transaction = new Transaction(doc))
      {
        if (transaction.Start("Draw first suggestion") == TransactionStatus.Started)
        {
          DrawSequence(doc, suggest_seqs[0]);

          // reset visibility settings
          // green_ls_cat.set_Visible(view, false);
          // yellow_ls_cat.set_Visible(view, false);
          // red_ls_cat.set_Visible(view, false);

          if (TransactionStatus.Committed != transaction.Commit())
          {
            TaskDialog.Show("Failure", "Transaction could not be committed");
          }
        }
      }

      Instance.autocomplete = true;
    }

    public void DrawAllSuggestions(Document doc, View view)
    {
      Instance.autocomplete = false;

      using (Transaction transaction = new Transaction(doc))
      {
        if (transaction.Start("Draw suggestions") == TransactionStatus.Started)
        {
          // we should have three sequences
          for (int seq_i = 0; seq_i < suggest_seqs.Count; seq_i++)
          {
            Sequence seq = suggest_seqs[seq_i];
            seq.eids.Clear();

            List<double> xyxy = seq.coords[0];

            // Create a geometry plane in Revit application
            int z = 100;
            XYZ normal = new XYZ(0, 0, 1);
            XYZ origin = new XYZ(0, 0, z);
            Plane geomPlane = Plane.CreateByNormalAndOrigin(normal, origin);

            // Create a sketch plane in current document
            SketchPlane sketch = SketchPlane.Create(doc, geomPlane);

            XYZ start = new XYZ(xyxy[0], xyxy[1], z);
            XYZ end = new XYZ(xyxy[2], xyxy[3], z);
            Line geom_line = Line.CreateBound(start, end);

            // Create a ModelArc element using the created geometry arc and sketch plane
            ModelLine line = doc.Create.NewModelCurve(geom_line, sketch) as ModelLine;

            // change its color and weight
            if (seq_i == 0)
              line.LineStyle = green_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);
            else if (seq_i == 1)
              line.LineStyle = yellow_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);
            else if (seq_i == 2)
              line.LineStyle = red_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);

            seq.eids.Add(line.Id);
          }

          // reset visibility settings
          // green_ls_cat.set_Visible(view, true);
          // yellow_ls_cat.set_Visible(view, true);
          // red_ls_cat.set_Visible(view, true);

          if (TransactionStatus.Committed != transaction.Commit())
          {
            TaskDialog.Show("Failure", "Transaction could not be committed");
          }
        }
      }

      Instance.autocomplete = true;
    }

    public void DrawCircle(Document doc, XYZ center)
    {
      // center's z value should be 0, to be on the plane
      XYZ new_center = new XYZ(center.X, center.Y, 100);

      // draw a circle
      double radius = 0.4;
      double startAngle = 0;      // In radian
      double endAngle = 2 * Math.PI;        // In radian
      XYZ xAxis = new XYZ(1, 0, 0);   // The x axis to define the arc plane. Must be normalized
      XYZ yAxis = new XYZ(0, 1, 0);   // The y axis to define the arc plane. Must be normalized

      using (Transaction transaction = new Transaction(doc))
      {
        if (transaction.Start("Draw circle") == TransactionStatus.Started)
        {
          Arc geom_arc = Arc.Create(new_center, radius, startAngle, endAngle, xAxis, yAxis);

          // Create a geometry plane in Revit application
          Plane geomPlane = Plane.CreateByNormalAndOrigin(new XYZ(0, 0, 1), new XYZ(0, 0, 100));

          // Create a sketch plane in current document
          SketchPlane sketch = SketchPlane.Create(doc, geomPlane);

          // Create a ModelArc element using the created geometry arc and sketch plane
          ModelArc arc = doc.Create.NewModelCurve(geom_arc, sketch) as ModelArc;

          // change its color and weight
          arc.LineStyle = corner_ls_cat.GetGraphicsStyle(GraphicsStyleType.Projection);

          if (TransactionStatus.Committed != transaction.Commit())
          {
              TaskDialog.Show("Failure", "Transaction could not be committed");
          }

          Instance.new_corner_eids.Add(arc.Id);
        }
      }
    }

    public static XYZ TransformPoint(XYZ point, Transform transform)
    {
        double x = point.X;
        double y = point.Y;
        double z = point.Z;

        //transform basis of the old coordinate system in the new coordinate // system
        XYZ b0 = transform.get_Basis(0);
        XYZ b1 = transform.get_Basis(1);
        XYZ b2 = transform.get_Basis(2);
        XYZ origin = transform.Origin;

        //transform the origin of the old coordinate system in the new 
        //coordinate system
        double xTemp = x * b0.X + y * b1.X + z * b2.X + origin.X;
        double yTemp = x * b0.Y + y * b1.Y + z * b2.Y + origin.Y;
        double zTemp = x * b0.Z + y * b1.Z + z * b2.Z + origin.Z;

        return new XYZ(xTemp, yTemp, zTemp);
    }

    public void SaveTransform(Document active_doc)
    {
        // get the point cloud object
        FilteredElementCollector collector = new FilteredElementCollector(active_doc).OfClass(typeof(PointCloudInstance));
        IList<Element> elements = collector.ToElements();
        Debug.Assert(elements.Count == 1);
        PointCloudInstance pc = elements[0] as PointCloudInstance;

        // get the point cloud transform
        Transform t = pc.GetTotalTransform();
        XYZ b0 = t.get_Basis(0);
        XYZ b1 = t.get_Basis(1);
        XYZ b2 = t.get_Basis(2);
        XYZ origin = t.Origin;
        double scale = t.Scale;

        // get the point cloud crop, which should be the section box
        View3D view = active_doc.ActiveView as View3D;
        BoundingBoxXYZ bbox = view.GetSectionBox();
        XYZ bbox_min = TransformPoint(bbox.Min, bbox.Transform);
        XYZ bbox_max = TransformPoint(bbox.Max, bbox.Transform);

        string t_str = "";
        t_str += String.Format("b0,{0},{1},{2}\n", b0.X, b0.Y, b0.Z);
        t_str += String.Format("b1,{0},{1},{2}\n", b1.X, b1.Y, b1.Z);
        t_str += String.Format("b2,{0},{1},{2}\n", b2.X, b2.Y, b2.Z);
        t_str += String.Format("origin,{0},{1},{2}\n", origin.X, origin.Y, origin.Z);
        t_str += String.Format("scale,{0}\n", scale);
        t_str += String.Format("bbox_min,{0},{1},{2}\n", bbox_min.X, bbox_min.Y, bbox_min.Z);
        t_str += String.Format("bbox_max,{0},{1},{2}\n", bbox_max.X, bbox_max.Y, bbox_max.Z);

        string curr_save_root = Path.GetDirectoryName(active_doc.PathName);
        string transform_f = String.Format("{0}\\transforms.txt",
                                           curr_save_root, active_doc.Title);
        File.WriteAllText(transform_f, t_str);
    }
  }
}
