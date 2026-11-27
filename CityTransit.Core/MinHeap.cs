using System;

namespace CityTransit.Core;

internal sealed class MinHeap
{
	private double[] _keys = new double[64];

	private int[] _values = new int[64];

	public int Count { get; private set; }

	public void Push(double key, int value)
	{
		if (Count == _keys.Length)
		{
			Array.Resize(ref _keys, Count * 2);
			Array.Resize(ref _values, Count * 2);
		}
		int num = Count++;
		while (num > 0)
		{
			int num2 = (num - 1) / 2;
			if (_keys[num2] <= key)
			{
				break;
			}
			_keys[num] = _keys[num2];
			_values[num] = _values[num2];
			num = num2;
		}
		_keys[num] = key;
		_values[num] = value;
	}

	public double Pop(out int value)
	{
		double result = _keys[0];
		value = _values[0];
		Count--;
		if (Count > 0)
		{
			double num = _keys[Count];
			int num2 = _values[Count];
			int num3 = 0;
			while (true)
			{
				int num4 = num3 * 2 + 1;
				if (num4 >= Count)
				{
					break;
				}
				if (num4 + 1 < Count && _keys[num4 + 1] < _keys[num4])
				{
					num4++;
				}
				if (_keys[num4] >= num)
				{
					break;
				}
				_keys[num3] = _keys[num4];
				_values[num3] = _values[num4];
				num3 = num4;
			}
			_keys[num3] = num;
			_values[num3] = num2;
		}
		return result;
	}
}
