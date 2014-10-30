﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2014
using NUnit.Framework;
using AqlaSerializer;
using AqlaSerializer.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Examples.Issues
{
    [TestFixture]
    public class Issue367
    {
        [ProtoBuf.ProtoContract]
        public class TestClass
        {
            [ProtoBuf.ProtoMember(1)]
            public string Id { get; set; }
        }

#if DEBUG
        [Test]
        public void LockContention_DTO()
        {
            var model = TypeModel.Create();
            Func<object, byte[]> serialize = obj =>
            {
                using (var ms = new MemoryStream())
                {
                    model.Serialize(ms, obj);
                    return ms.ToArray();
                }
            };
            var tasks = new List<Task>(50000);
            for (var i = 0; i < 50000; i++)
            {
                tasks.Add(Task.Factory.StartNew(() => serialize(new TestClass { Id = Guid.NewGuid().ToString() })));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.LessOrEqual(1, 2, "because I always get this backwards");
            Assert.LessOrEqual(model.LockCount, 50);
        }

        [Test]
        public void LockContention_BasicType()
        {
            var model = TypeModel.Create();
            Func<object, byte[]> serialize = obj =>
            {
                using (var ms = new MemoryStream())
                {
                    model.Serialize(ms, obj);
                    return ms.ToArray();
                }
            };
            var tasks = new List<Task>(50000);
            for (var i = 0; i < 50000; i++)
            {
                tasks.Add(Task.Factory.StartNew(() => serialize(Guid.NewGuid().ToString())));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.LessOrEqual(1, 2, "because I always get this backwards");
            Assert.LessOrEqual(model.LockCount, 50);
        }

        [Test]
        public void LockContention_Dictionary()
        {
            var model = TypeModel.Create();
            Func<object, byte[]> serialize = obj =>
            {
                using (var ms = new MemoryStream())
                {
                    model.Serialize(ms, obj);
                    return ms.ToArray();
                }
            };
            var tasks = new List<Task>(50000);
            Dictionary<string, int> d = new Dictionary<string, int>
            {
                { "abc", 123}, {"def", 456}
            };
            for (var i = 0; i < 50000; i++)
            {
                tasks.Add(Task.Factory.StartNew(state => serialize(state.ToString()), d));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.LessOrEqual(1, 2, "because I always get this backwards");
            Assert.LessOrEqual(model.LockCount, 50);
        }
#endif
    }
}
