import ultralytics
import ultralytics.nn.modules.block as block

print("ultralytics version:", ultralytics.__version__)
names = dir(block)
print("block exports:", [name for name in names if name.startswith("C3")])
print("Has PSABlock?", "PSABlock" in names)
print("Has Bottleneck?", "Bottleneck" in names)

