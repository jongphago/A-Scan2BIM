import os
import numpy as np


# load full density image
def get_density_slices(data_path, floor_name):
    density_slices = []
    for slice_i in range(7):
        slice_f = f"{data_path}/density/{floor_name}/density_{slice_i:02d}.npy"
        density_slice = np.load(slice_f)
        density_slices.append(density_slice)
    return density_slices


def stack_density_slices(density_slices):
    def normalize_density(density_slice):
        # drop bottom and top 5 percentile for density map
        counts = sorted(density_slice[density_slice > 0])
        lower = np.percentile(counts, q=10)
        upper = np.percentile(counts, q=90)

        density_slice = np.maximum(density_slice, lower)
        density_slice = np.minimum(density_slice, upper)
        density_slice -= lower
        density_slice /= upper - lower

        return density_slice

    # Normalize density maps
    density_full = [
        normalize_density(np.sum(density_slices[:4], axis=0)),
        normalize_density(density_slices[4]),
        normalize_density(np.sum(density_slices[5:7], axis=0)),
    ]
    return np.stack(density_full, axis=2)


# we need to square pad the image
def padding_density_full(density_full):
    (h, w, _) = density_full.shape
    side_len = max(h, w)

    pad_h_before = (side_len - h) // 2
    pad_h_after = side_len - h - pad_h_before
    pad_w_before = (side_len - w) // 2
    pad_w_after = side_len - w - pad_w_before

    density_full = np.pad(  # (1462, 1462, 3)
        density_full,
        [
            [pad_h_before, pad_h_after],
            [pad_w_before, pad_w_after],
            [0, 0],
        ],
    )
    return density_full


if __name__ == "__main__":
    floor_idx = 11
    data_path = "quickstart/data"

    floor_f = os.path.join(data_path, "all_floors.txt")
    with open(floor_f, "r") as f:
        floor_names = [x.strip() for x in f.readlines()]
    floor_name = floor_names[floor_idx].split(",")[0]  # 32_ShortOffice_05_F2

    density_slices = get_density_slices(data_path, floor_name)
    density_full = stack_density_slices(density_slices)
    density_full = padding_density_full(density_full)
    assert density_full.shape == (1462, 1462, 3)
