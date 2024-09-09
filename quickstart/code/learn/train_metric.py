import argparse
import datetime
import os
import time
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import DataLoader
from torch.utils.tensorboard import SummaryWriter

import my_utils
import utils.misc as utils
from datasets.building_corners_full import (
    BuildingCornerDataset,
    collate_fn_seq,
    get_pixel_features,
)
from models.corner_models import CornerEnum
from models.corner_to_edge import prepare_edge_data
from models.edge_full_models import EdgeEnum
from models.loss import CornerCriterion, EdgeCriterion
from models.unet import ResNetBackbone


def vis_triplets(data, triplets):
    image = data["img"][0].permute(1, 2, 0).cpu().numpy()
    image -= image.min()
    image /= image.max()
    image = np.max(image, axis=2)

    edge_coords = data["edge_coords"][0].cpu().numpy()
    edge_labels = data["edge_labels"][0].cpu().numpy()

    for (anc_i, pos_i, neg_i) in triplets:
        plt.imshow(image, cmap="gray")

        for edge_i, (x0, y0, x1, y1) in enumerate(edge_coords):
            if edge_labels[edge_i] == 0:
                assert edge_i not in triplets
                plt.plot([x0, x1], [y0, y1], "--c")

        (x0, y0, x1, y1) = edge_coords[anc_i]
        plt.plot([x0, x1], [y0, y1], "-y")

        (x0, y0, x1, y1) = edge_coords[pos_i]
        plt.plot([x0, x1], [y0, y1], "-g")

        (x0, y0, x1, y1) = edge_coords[neg_i]
        plt.plot([x0, x1], [y0, y1], "-r")

        plt.axis("off")
        plt.tight_layout()
        plt.show()


def get_args_parser():
    parser = argparse.ArgumentParser("Set transformer detector", add_help=False)
    parser.add_argument("--lr", default=2e-4, type=float)
    parser.add_argument("--batch_size", default=64, type=int)
    parser.add_argument("--weight_decay", default=1e-5, type=float)
    parser.add_argument("--epochs", default=150, type=int)
    parser.add_argument("--lr_drop", default=50, type=int)
    parser.add_argument(
        "--clip_max_norm", default=0.1, type=float, help="gradient clipping max norm"
    )

    # Model parameters
    parser.add_argument(
        "--frozen_weights",
        type=str,
        default=None,
        help="Path to the pretrained model. If set, only the mask head will be trained",
    )
    # * Backbone
    parser.add_argument(
        "--backbone",
        default="resnet50",
        type=str,
        help="Name of the convolutional backbone to use",
    )
    parser.add_argument(
        "--dilation",
        action="store_true",
        help="If true, we replace stride with dilation in the last convolutional block (DC5)",
    )
    parser.add_argument(
        "--position_embedding",
        default="sine",
        type=str,
        choices=("sine", "learned"),
        help="Type of positional embedding to use on top of the image features",
    )

    # * Transformer
    parser.add_argument(
        "--enc_layers",
        default=6,
        type=int,
        help="Number of encoding layers in the transformer",
    )
    parser.add_argument(
        "--dec_layers",
        default=6,
        type=int,
        help="Number of decoding layers in the transformer",
    )
    parser.add_argument(
        "--dim_feedforward",
        default=2048,
        type=int,
        help="Intermediate size of the feedforward layers in the transformer blocks",
    )
    parser.add_argument(
        "--hidden_dim",
        default=256,
        type=int,
        help="Size of the embeddings (dimension of the transformer)",
    )
    parser.add_argument(
        "--dropout", default=0.1, type=float, help="Dropout applied in the transformer"
    )
    parser.add_argument(
        "--nheads",
        default=8,
        type=int,
        help="Number of attention heads inside the transformer's attentions",
    )
    parser.add_argument(
        "--num_queries", default=100, type=int, help="Number of query slots"
    )
    parser.add_argument("--pre_norm", action="store_true")

    # * Segmentation
    parser.add_argument(
        "--masks",
        action="store_true",
        help="Train segmentation head if the flag is provided",
    )

    # Loss
    parser.add_argument(
        "--no_aux_loss",
        dest="aux_loss",
        action="store_false",
        help="Disables auxiliary decoding losses (loss at each layer)",
    )
    # * Matcher
    parser.add_argument(
        "--set_cost_class",
        default=1,
        type=float,
        help="Class coefficient in the matching cost",
    )
    parser.add_argument(
        "--set_cost_bbox",
        default=5,
        type=float,
        help="L1 box coefficient in the matching cost",
    )
    parser.add_argument(
        "--set_cost_giou",
        default=2,
        type=float,
        help="giou box coefficient in the matching cost",
    )
    # * Loss coefficient/
    parser.add_argument("--mask_loss_coef", default=1, type=float)
    parser.add_argument("--dice_loss_coef", default=1, type=float)
    parser.add_argument("--bbox_loss_coef", default=5, type=float)
    parser.add_argument("--giou_loss_coef", default=2, type=float)
    parser.add_argument(
        "--eos_coef",
        default=0.1,
        type=float,
        help="Relative classification weight of the no-object class",
    )

    # dataset parameters
    parser.add_argument("--dataset_file", default="coco")
    parser.add_argument("--coco_path", type=str)
    parser.add_argument("--coco_panoptic_path", type=str)
    parser.add_argument("--remove_difficult", action="store_true")

    parser.add_argument(
        "--output_dir",
        default="./ckpts_metric/debug",
        help="path where to save, empty for no saving",
    )
    parser.add_argument(
        "--corner_model", default="unet", help="path where to save, empty for no saving"
    )
    parser.add_argument(
        "--device", default="cuda", help="device to use for training / testing"
    )
    parser.add_argument("--seed", default=42, type=int)
    parser.add_argument("--resume", default="", help="resume from checkpoint")
    parser.add_argument(
        "--start_epoch", default=0, type=int, metavar="N", help="start epoch"
    )
    parser.add_argument("--eval", action="store_true")
    parser.add_argument("--num_workers", default=2, type=int)

    # my own
    parser.add_argument("--test_idx", type=int, default=0)
    parser.add_argument("--grad_accum", type=int, default=16)
    parser.add_argument("--threshold", type=int, default=30)
    parser.add_argument("--deform_type", default="DETR_dense")
    parser.add_argument("--num_samples", type=int, default=8)
    parser.add_argument("--pool_type", default="max")

    return parser


def train_one_epoch(
    backbone,
    edge_model,
    edge_criterion,
    dist_fn,
    data_loader,
    optimizer,
    epoch,
    max_norm,
    args,
):
    backbone.train()
    edge_model.train()
    edge_criterion.train()
    optimizer.zero_grad()

    metric_logger = utils.MetricLogger(delimiter="  ")
    metric_logger.add_meter(
        "lr", utils.SmoothedValue(window_size=100, fmt="{value:.6f}")
    )
    header = "Epoch: [{}]".format(epoch)
    print_freq = 40

    batch_i = 0
    for data in metric_logger.log_every(data_loader, print_freq, header):
        loss_hb, loss_rel, acc_hb, acc_rel = run_model(
            data,
            backbone,
            edge_model,
            epoch,
            edge_criterion,
            dist_fn,
            args,
        )

        # compute loss
        loss = loss_hb + loss_rel
        loss /= args.grad_accum
        loss.backward()

        # collect stats
        loss_dict = {
            "loss_hb": loss_hb,
            "loss_rel": loss_rel,
            "acc_hb": acc_hb,
            "acc_rel": acc_rel,
        }
        loss_value = loss.item()
        metric_logger.update(loss=loss_value, **loss_dict)
        metric_logger.update(lr=optimizer.param_groups[0]["lr"])

        if ((batch_i + 1) % args.grad_accum == 0) or (
            (batch_i + 1) == len(data_loader)
        ):
            if max_norm > 0:
                torch.nn.utils.clip_grad_norm_(backbone.parameters(), max_norm)
                torch.nn.utils.clip_grad_norm_(edge_model.parameters(), max_norm)

            optimizer.step()
            optimizer.zero_grad()

        batch_i += 1

    print("Averaged stats:", metric_logger)
    return {k: meter.global_avg for k, meter in metric_logger.meters.items()}


def run_model(
    data,
    backbone,
    edge_model,
    epoch,
    edge_criterion,
    dist_fn,
    args,
):
    image = data["img"].cuda()
    image_feats, feat_mask, all_image_feats = backbone(image)

    edge_coords = data["edge_coords"].cuda()
    edge_mask = data["edge_coords_mask"].cuda()
    edge_lengths = data["edge_coords_lengths"].cuda()
    edge_labels = data["edge_labels"].cuda()

    # flags to pass in, indicating condition and query edges
    edge_flags = torch.ones_like(edge_labels)
    edge_flags[edge_labels != 0] = 2

    (embed_hb, embed_rel) = edge_model(
        image_feats,
        feat_mask,
        edge_coords,
        edge_mask,
        edge_flags,
        None,
        None,
        skip_filtering=True,
        get_embed=True,
        mask_gt=False,
    )

    # make sure our batch size is 1
    assert len(embed_hb) == len(embed_rel) == 1

    # make sure we didn't change the ordering
    order = edge_labels[edge_flags == 2]
    assert (order.argsort() == order - 1).all()

    # generate and sample triplets
    triplets = my_utils.all_triplets[len(order)].copy()
    np.random.shuffle(triplets)
    triplets = triplets[:256]
    triplets += (edge_flags != 2).sum().item()  # offset by num of cond edges

    def loss_helper(anc, pos, neg):
        loss = edge_criterion(anc, pos, neg)

        anc2pos = dist_fn(anc, pos)
        anc2neg = dist_fn(anc, neg)
        acc = (anc2pos < anc2neg).float().mean()

        return loss, acc

    anc_hb = embed_hb[0, triplets[:, 0]]
    pos_hb = embed_hb[0, triplets[:, 1]]
    neg_hb = embed_hb[0, triplets[:, 2]]
    loss_hb, acc_hb = loss_helper(anc_hb, pos_hb, neg_hb)

    anc_rel = embed_rel[0, triplets[:, 0]]
    pos_rel = embed_rel[0, triplets[:, 1]]
    neg_rel = embed_rel[0, triplets[:, 2]]
    loss_rel, acc_rel = loss_helper(anc_rel, pos_rel, neg_rel)

    # vis_triplets(data, triplets)

    return loss_hb, loss_rel, acc_hb, acc_rel


@torch.no_grad()
def evaluate(
    backbone,
    edge_model,
    edge_criterion,
    dist_fn,
    data_loader,
    epoch,
    args,
):
    backbone.eval()
    edge_model.eval()
    edge_criterion.eval()
    metric_logger = utils.MetricLogger(delimiter="  ")
    header = "Test:"

    for data in metric_logger.log_every(data_loader, 10, header):
        loss_hb, loss_rel, acc_hb, acc_rel = run_model(
            data,
            backbone,
            edge_model,
            epoch,
            edge_criterion,
            dist_fn,
            args,
        )

        loss = loss_hb + loss_rel
        loss_value = loss.item()

        # collect stats
        loss_dict = {
            "loss_hb": loss_hb,
            "loss_rel": loss_rel,
            "acc_hb": acc_hb,
            "acc_rel": acc_rel,
        }
        metric_logger.update(loss=loss_value, **loss_dict)

    print("Averaged stats:", metric_logger)
    return {k: meter.global_avg for k, meter in metric_logger.meters.items()}


def main(args):
    DATAPATH = "./data/bim_dataset_big_v5"
    train_dataset = BuildingCornerDataset(
        DATAPATH,
        phase="train",
        rand_aug=True,
        test_idx=args.test_idx,
        multiplier=1000,
        batch_size=args.batch_size,
        threshold=args.threshold,
        task="metric_fix",
    )

    # for blah in train_dataset:
    #     pass

    test_dataset = BuildingCornerDataset(
        DATAPATH,
        phase="valid",
        rand_aug=False,
        test_idx=args.test_idx,
        multiplier=1,
        batch_size=args.batch_size,
        threshold=args.threshold,
        task="metric_fix",
    )

    train_dataloader = DataLoader(
        train_dataset,
        batch_size=1,
        shuffle=True,
        num_workers=8,
        collate_fn=collate_fn_seq,
    )
    test_dataloader = DataLoader(
        test_dataset,
        batch_size=1,
        shuffle=False,
        num_workers=8,
        collate_fn=collate_fn_seq,
    )

    backbone = ResNetBackbone()
    strides = backbone.strides
    num_channels = backbone.num_channels

    # backbone = nn.DataParallel(backbone)
    backbone = backbone.cuda()

    edge_model = EdgeEnum(
        input_dim=128,
        hidden_dim=256,
        num_feature_levels=4,
        backbone_strides=strides,
        backbone_num_channels=num_channels,
        deform_type=args.deform_type,
        num_samples=args.num_samples,
        pool_type=args.pool_type,
    )
    # edge_model = nn.DataParallel(edge_model)
    edge_model = edge_model.cuda()

    dist_fn = lambda x, y: 1.0 - f.cosine_similarity(x, y)
    edge_criterion = nn.TripletMarginWithDistanceLoss(
        distance_function=dist_fn, reduction="sum"
    )

    backbone_params = [p for p in backbone.parameters()]
    edge_params = [p for p in edge_model.parameters()]

    all_params = edge_params + backbone_params
    optimizer = torch.optim.AdamW(
        all_params, lr=args.lr, weight_decay=args.weight_decay
    )
    lr_scheduler = torch.optim.lr_scheduler.StepLR(optimizer, args.lr_drop)
    start_epoch = args.start_epoch

    if args.resume:
        ckpt = torch.load(args.resume)
        backbone.load_state_dict(ckpt["backbone"])
        edge_model.load_state_dict(ckpt["edge_model"])
        optimizer.load_state_dict(ckpt["optimizer"])
        lr_scheduler.load_state_dict(ckpt["lr_scheduler"])
        lr_scheduler.step_size = args.lr_drop

        print(
            "Resume from ckpt file {}, starting from epoch {}".format(
                args.resume, ckpt["epoch"]
            )
        )
        start_epoch = ckpt["epoch"] + 1

    else:
        pretrained_root = "ckpts_vml/compare/03_10_dense_aug_16_max_mask"
        ckpt = torch.load("%s/%d/checkpoint.pth" % (pretrained_root, args.test_idx))

        backbone.load_state_dict(ckpt["backbone"])
        edge_model.load_state_dict(ckpt["edge_model"])

        print("Resume from pre-trained checkpoints")

    n_backbone_parameters = sum(p.numel() for p in backbone_params if p.requires_grad)
    n_edge_parameters = sum(p.numel() for p in edge_params if p.requires_grad)
    n_all_parameters = sum(p.numel() for p in all_params if p.requires_grad)
    print("number of trainable backbone params:", n_backbone_parameters)
    print("number of trainable edge params:", n_edge_parameters)
    print("number of all trainable params:", n_all_parameters)

    print("Start training")
    start_time = time.time()

    output_dir = Path("%s/%d" % (args.output_dir, args.test_idx))
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)

    # prepare summary writer
    writer = SummaryWriter(log_dir=output_dir)

    best_acc = 0
    for epoch in range(start_epoch, args.epochs):
        train_stats = train_one_epoch(
            backbone,
            edge_model,
            edge_criterion,
            dist_fn,
            train_dataloader,
            optimizer,
            epoch,
            args.clip_max_norm,
            args,
        )
        lr_scheduler.step()

        val_stats = evaluate(
            backbone,
            edge_model,
            edge_criterion,
            dist_fn,
            test_dataloader,
            epoch,
            args,
        )

        val_acc = (val_stats["acc_hb"] + val_stats["acc_rel"]) / 2

        if val_acc > best_acc:
            is_best = True
            best_acc = val_acc
        else:
            is_best = False

        # write out stats
        for key, value in train_stats.items():
            writer.add_scalar("train/%s" % key, value, epoch)
        for key, value in val_stats.items():
            writer.add_scalar("val/%s" % key, value, epoch)

        if args.output_dir:
            checkpoint_paths = [output_dir / "checkpoint.pth"]
            if is_best:
                checkpoint_paths.append(output_dir / "checkpoint_best.pth")

            for checkpoint_path in checkpoint_paths:
                torch.save(
                    {
                        "backbone": backbone.state_dict(),
                        "edge_model": edge_model.state_dict(),
                        "optimizer": optimizer.state_dict(),
                        "lr_scheduler": lr_scheduler.state_dict(),
                        "epoch": epoch,
                        "args": args,
                        "val_acc": val_acc,
                    },
                    checkpoint_path,
                )

    total_time = time.time() - start_time
    total_time_str = str(datetime.timedelta(seconds=int(total_time)))
    print("Training time {}".format(total_time_str))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        "GeoVAE training and evaluation script", parents=[get_args_parser()]
    )
    args = parser.parse_args()
    main(args)
