import ultralytics
import ultralytics.nn.modules.block as block

print("ultralytics version:", ultralytics.__version__)
print("Has C2PSA?", hasattr(block, "C2PSA"))
print("Has PSABlock?", hasattr(block, "PSABlock"))
print("PSA-related names:", [name for name in dir(block) if "PSA" in name])

