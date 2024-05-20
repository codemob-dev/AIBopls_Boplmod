using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;


namespace AI
{
    internal class Network
    {
        public List<Layer> layers = [];
        public Network()
        {

        }
        public Network(params Layer[] layers) : this()
        {
            foreach (Layer layer in layers)
            {
                AddLayer(layer);
            }
        }

        public Network AddLayer(Layer layer)
        {
            layers.Add(layer);
            return this;
        }
        public int PreviousLayerSize {  get { return layers.Last().nodes.Count; } }

        public Network Randomize(double factor)
        {
            return new Network
            {
                layers = layers.Select(x => x.Randomize(factor)).ToList()
            };
        }

        public List<Network> GenRandomBatch(int batchSize, double randomFactor)
        {
            List<Network> batch = new(batchSize + 1)
            {
                this
            };
            for (int i = 0; i < batchSize; i++)
            {
                batch.Add(Randomize(randomFactor));
            }
            return batch;
        }

        public List<double> Evaluate(List<double> input)
        {
            foreach (Layer layer in layers)
            {
                input = layer.Evaluate(input);
            }
            return input;
        }
        public void Save(Stream stream)
        {
            var binaryWriter = new BinaryWriter(stream);
            binaryWriter.Write(layers.Count);
            foreach (var layer in layers)
            {
                binaryWriter.Write(layer.GetType().AssemblyQualifiedName);
                layer.Save(binaryWriter);
            }
        }
        public void Save(string filename)
        {
            filename = Path.ChangeExtension(filename, ".bplnet");
            using var file = File.Create(filename);
            Save(file);
        }
        public static Network Load(string filename)
        {
            filename = Path.ChangeExtension(filename, ".bplnet");
            using var file = File.OpenRead(filename);
            return Load(file);
        }
        public static Network Load(Stream stream)
        {
            var binaryReader = new BinaryReader(stream);
            var numLayers = binaryReader.ReadInt32();

            var net = new Network();
            net.layers.Capacity = numLayers;

            for (int i = 0; i < numLayers; i++)
            {
                var layerType = Type.GetType(binaryReader.ReadString());
                var layer = layerType.GetConstructor([]).Invoke([]) as Layer;
                layer.Load(binaryReader);
                net.AddLayer(layer);
            }
            return net;
        }
    }
    internal abstract class Layer()
    {
        public List<Node> nodes = [];
        public abstract List<double> Evaluate(List<double> input);
        public Layer Randomize(double factor)
        {
            var layer = GetType()
                .GetConstructor([])
                .Invoke([]) as Layer;
            layer.nodes = nodes.Select(x => x.Randomize(factor)).ToList();
            return layer;
        }
        public void Load(BinaryReader binaryReader)
        {
            var numNodes = binaryReader.ReadInt32();
            nodes = new List<Node>(numNodes);
            for (int i = 0; i < numNodes; i++)
            {
                var nodeType = Type.GetType(binaryReader.ReadString());
                var node = nodeType
                    .GetConstructor([typeof(List<double>), typeof(double)])
                    .Invoke([new List<double>(), 0.0]) as Node;
                node.Load(binaryReader);
                nodes.Add(node);
            }

        }
        public void Save(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(nodes.Count);
            foreach (var node in nodes)
            {
                binaryWriter.Write(node.GetType().AssemblyQualifiedName);
                node.Save(binaryWriter);
            }
        }
    }
    internal class InputLayer() : Layer()
    {
        public InputLayer(Type nodeType, int length) : this()
        {
            if (!nodeType.IsSubclassOf(typeof(Node)))
            {
                throw new ArgumentException($"{nodeType.Name} does not extend Node!");
            }
            for (int i = 0; i < length; i++)
            {
                nodes.Add(nodeType.GetConstructor([typeof(int)]).Invoke([1]) as Node);
            }
        }

        public override List<double> Evaluate(List<double> input)
        {
            if (nodes.Count != input.Count)
            {
                throw new IndexOutOfRangeException(
                    "The number of inputs does not match the number of input nodes!");
            }
            List<double> result = [];
            for (int i = 0; i < nodes.Count; i++)
            {
                result.Add(nodes[i].Evaluate([input[i]]));
            }
            return result;
        }
    }
    internal class HiddenLayer() : Layer()
    {

        public HiddenLayer(Type nodeType, int length, int previousLength) : this()
        {
            if (!nodeType.IsSubclassOf(typeof(Node)))
            {
                throw new ArgumentException($"{nodeType.Name} does not extend Node!");
            }
            for (int i = 0; i < length; i++)
            {
                nodes.Add(nodeType.GetConstructor([typeof(int)]).Invoke([previousLength]) as Node);
            }
        }

        public override List<double> Evaluate(List<double> input)
        {
            List<double> result = [];
            foreach (Node node in nodes)
            {
                result.Add(node.Evaluate(input));
            }
            return result;
        }
    }
    internal class OutputLayer() : Layer()
    {
        public override List<double> Evaluate(List<double> input)
        {
            List<double> result = [];
            foreach (Node node in nodes)
            {
                result.Add(node.Evaluate(input));
            }
            return result;
        }

        public OutputLayer(List<Type> nodeTypes, int previousLength) : this()
        {
            foreach (var nodeType in nodeTypes)
            {
                if (!nodeType.IsSubclassOf(typeof(Node)))
                {
                    throw new ArgumentException($"{nodeType.Name} does not extend Node!");
                }
                nodes.Add(nodeType.GetConstructor([typeof(int)]).Invoke([previousLength]) as Node);
            }
        }
    }

    internal abstract class Node
    {
        public List<double> weights;
        public double bias;

        public Node(int numInputs)
        {
            weights = Enumerable.Repeat(0.0, numInputs)
                .Select(v=>2 * Random.Shared.NextDouble() - 1).ToList();
            bias = 2 * Random.Shared.NextDouble() - 1;
        }
        public Node(List<double> weights, double bias)
        {
            this.weights = weights;
            this.bias = bias;
        }

        public Node Randomize(double factor)
        {
            var clone = Clone();
            clone.weights = clone.weights
                .Select(d => d + 2 * factor * Random.Shared.NextDouble() - factor)
                .ToList();
            clone.bias = 2 * factor * Random.Shared.NextDouble() - factor;
            return clone;
        }
        public double Evaluate(List<double> input)
        {
            var weightedAverage = input.Select((dbl, i) => dbl * weights[i]).Sum();
            var activation = ActivationFunc(weightedAverage + bias);
            if (double.IsNaN(activation)) return 0;
            return activation;
        }
        public abstract double ActivationFunc(double x);

        public Node Clone()
        {
            return GetType()
                .GetConstructor([typeof(List<double>), typeof(double)])
                .Invoke([weights, bias]) as Node;
        }

        internal void Save(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(weights.Count);
            foreach (var weight in weights)
            {
                binaryWriter.Write(weight);
            }
            binaryWriter.Write(bias);
        }

        internal void Load(BinaryReader binaryReader)
        {
            var numWeights = binaryReader.ReadInt32();
            weights = new List<double>(numWeights);
            for (int i = 0; i < numWeights; i++)
            {
                weights.Add(binaryReader.ReadDouble());
            }
            bias = binaryReader.ReadDouble();
        }
    }
    internal class SigmoidNode : Node
    {
        public SigmoidNode(int numInputs) : base(numInputs)
        {

        }
        public SigmoidNode(List<double> weights, double bias) : base(weights, bias)
        {

        }
        public override double ActivationFunc(double x)
        {
            var k = Math.Exp(x);
            return k / (1.0 + k);
        }
    }
    internal class TanhNode : Node
    {
        public TanhNode(int numInputs) : base(numInputs)
        {

        }
        public TanhNode(List<double> weights, double bias) : base(weights, bias)
        {

        }
        public override double ActivationFunc(double x)
        {
            return Math.Tanh(x);
        }
    }

    internal class IdentityNode : Node
    {
        public IdentityNode(int numInputs) : base(numInputs)
        {

        }
        public IdentityNode(List<double> weights, double bias) : base(weights, bias)
        {

        }
        public override double ActivationFunc(double x)
        {
            return x;
        }
    }
}