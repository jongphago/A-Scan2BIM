``` python
data/
├── revit_projects  /32_ShortOffice_05_F2.rvt  
├── transforms      /32_ShortOffice_05_F2.txt
├── laz             /32_ShortOffice_05_F2.laz
├── bounds          /32_ShortOffice_05_F2.csv  
├── annot           /32_ShortOffice_05_F2.json  
├── pred_corners    /32_ShortOffice_05_F2.json 
├── density         /32_ShortOffice_05_F2     /density_0N.npy  # 0~6 slices
├── history         /32_ShortOffice_05_F2     /my_journal.000N.txt
├── recap_projects  /32_ShortOffice_05_F2     /32_ShortOffice_05_F2_s0p01m.rcp
└── all_floors.txt
```

```mermaid
timeline
  title Backend::demo_floor structure
  section Initialize Backend
    Data generation
      : ◀️ LAZ file (PCD)
      : data_gen.process_floor
      : data_gen.process_model
      : ✅ bounds_f.csv
      : ✅ annot_f.json

    Initialize density and bounds (demo_user)
      : ◀️ LAZ file (PCD)
      : ◀️ transformation matrix (Revit)
      : my_utils.process_laz
      : crop_pc_z
      : ✅ density_slice.npy
      : ✅ bounds.npy

  section Initilize data and models
    Initialize floor data
      : self.init_floor
      : ◀️ density_slice.npy
      : ✅ self.density_full
      : ◀️ bounds.csv
      : ◀️ annot.json
      : ✅ self.gt_corners
      : self.init_corner_models
      : self.get_pred_corners
      : ◀️ pred_corners.json
      : ✅ self.cached_corners

    Initialize corner models
      : self.init_corner_models
      : ❇️ self.corner_backbone
      : ❇️ self.corner_model

    Initialize edge models
      : self.init_edge_models
      : ❇️ self.edge_backbone
      : ❇️ self.edge_model

    Initialize metric models
      : self.init_metric_model
      : ❇️ self.metric_model

    Extract image features
      : self.cache_image_feats
      : ◀️ self.cached_corners
      : ◀️ self.density_full
      : self.edge_backbone
      : ✅ self.image_feats

  section start_server
    Sending HEAT predictions
      : self.get_pred_coords
      : self.edge_model
      : ◀️ self.cached_corners
      : ✅ heat_coords
      : ✅ heat_widths
    Autocomplete
      : self.run_autocomplete
      : self.get_pred_coords
      : self.run_metric_model
      : ◀️ edge_coords
      : ◀️ edge_order
```

```mermaid
timeline
  title Backend::demo_floor structure
  section Initialize Backend
    Data generation
      : data_gen.process_floor

    Initialize density and bounds (demo_user)
      : my_utils.process_laz

  section Initilize data and models
    Initialize floor data
      : self.init_floor
      : Load floor name
      : Load boundary of the floor
      : Load full density image
      : Pad density image to square
      : Load GT annotation
      : Load predicted corners

    Initialize corner models
      : self.init_corner_models
      : Load corner backbone
      : Load corner model

    Initialize edge models
      : self.init_edge_models
      : Load edge backbone
      : Load edge model

    Initialize metric models
      : self.init_metric_model
      : Load metric model

    Extract image features
      : self.cache_image_feats
      : Extract image features

  section start_server
    Sending HEAT predictions
      : self.get_pred_coords
      : Predict coordinates with edge model
    Autocomplete
      : self.run_autocomplete
```
