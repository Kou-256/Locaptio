from pathlib import Path
import onnx
from onnx import helper
source = Path('models/translation-onnx/encoder_model.onnx')
model = onnx.load(source)
nodes = []
for node in model.graph.node:
    if node.op_type == 'Constant' and len(node.attribute) == 1 and node.attribute[0].type == onnx.AttributeProto.TENSOR:
        tensor = node.attribute[0].t
        tensor.name = node.output[0]
        model.graph.initializer.append(tensor)
    elif node.op_type == 'ConstantOfShape':
        name = node.output[0] + '_fill_value'
        tensor = node.attribute[0].t
        tensor.name = name
        model.graph.initializer.append(tensor)
        nodes.append(helper.make_node('Expand', [name, node.input[0]], list(node.output), name=node.name))
    else:
        nodes.append(node)
del model.graph.node[:]
model.graph.node.extend(nodes)
onnx.checker.check_model(model)
target = Path('models/translation-npu-onnx')
target.mkdir(exist_ok=True)
onnx.save(model, target / 'encoder_model.onnx')
print('Prepared encoder with tensor constants as initializers')
