#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
#endregion

namespace RevitAssistant
{
  [Transaction( TransactionMode.Manual )]
  public class ObtainPrediction : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      App.Instance.ObtainPrediction(doc, view);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class AddCorner : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;
      UIView uiview = App.GetActiveUiView(uidoc);

      XYZ mouse_loc = App.Instance.GetMouseLoc(doc, view, uiview);
      App.Instance.DrawCircle(doc, mouse_loc);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class SendNewCorners : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      App.Instance.SendNewCorners(doc, view, uidoc);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class ToggleAutocomplete : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      App.Instance.ToggleAutocomplete(uiapp);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class AcceptSuggestion : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      App.Instance.AcceptSuggestion(doc, view, uidoc);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class AcceptSuggestionGreen : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      if (!App.Instance.mode_select)
        App.Instance.FollowSuggestion(doc);
      else
        App.Instance.AcceptSuggestion(doc, view, uidoc, 0);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class AcceptSuggestionYellow : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      if (!App.Instance.mode_select)
        App.Instance.DeclineSuggestion(doc, view, uidoc);
      else
        App.Instance.AcceptSuggestion(doc, view, uidoc, 1);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class AcceptSuggestionRed : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      if (App.Instance.mode_select)
        App.Instance.AcceptSuggestion(doc, view, uidoc, 2);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class FullAuto : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;

      Document doc = uidoc.Document;
      View view = doc.ActiveView;

      App.Instance.ToggleFullAuto(uiapp);

      return Result.Succeeded;
    }
  }

  [Transaction( TransactionMode.Manual )]
  public class SaveTransform: IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Document doc = uidoc.Document;

      App.Instance.SaveTransform(doc);

      return Result.Succeeded;
    }
  }
}
