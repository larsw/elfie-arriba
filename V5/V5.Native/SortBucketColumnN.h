#pragma once
using namespace System;

public ref class SortBucketColumnN
{
public:
	static void Bucket(array<Int64>^ values, int index, int length, array<Int64>^ bucketMins, array<Byte>^ rowBucketIndex, array<Int32>^ countPerBucket);
	static int BucketIndex(array<Int64>^ bucketMins, Int64 value);
};
