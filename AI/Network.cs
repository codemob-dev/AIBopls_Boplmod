﻿using System;
using System.Collections.Generic;
using System.Linq;


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

        public List<double> Evaluate(List<double> input)
        {
            foreach (Layer layer in layers)
            {
                input = layer.Evaluate(input);
            }
            return input;
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
            return ActivationFunc(weightedAverage + bias);
        }
        public abstract double ActivationFunc(double x);

        public Node Clone()
        {
            return GetType()
                .GetConstructor([typeof(List<double>), typeof(double)])
                .Invoke([weights, bias]) as Node;
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