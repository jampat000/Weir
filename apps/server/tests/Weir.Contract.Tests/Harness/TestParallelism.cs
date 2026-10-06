// Every test class runs a real server of its own, so classes running side by side compete for the machine. More than a
// handful at once on a big machine made each of them slower than running fewer (a 16-core PC finished the Libraries
// area in 100 seconds with six and needed 200 to 280 with sixteen, with requests timing out). On a smaller machine the
// runner's own default, one per processor, is already lower than this.
[assembly: CollectionBehavior(MaxParallelThreads = 6)]
