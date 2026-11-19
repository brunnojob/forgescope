"""Lexically scoped call candidates and local-import review leads.

This deliberately does not infer object types or execute module exports.
"""
import ast

def import_base(node, package):
    if not node.level:
        return node.module or ''
    parts = package.split('.') if package else []
    if node.level > len(parts):
        return None
    return '.'.join(parts[:len(parts)-node.level+1] + ([node.module] if node.module else []))

class Bindings(ast.NodeVisitor):
    def __init__(self, qualified, package):
        self.names, self.globals, self.nonlocals = {}, set(), set()
        self.qualified, self.package = qualified, package

    def bind(self, name, value=None):
        if name in self.names and self.names[name] != value:
            self.names[name] = None
        else:
            self.names[name] = value

    def visit_Name(self, node):
        if isinstance(node.ctx, (ast.Store, ast.Del)):
            self.bind(node.id)

    def visit_FunctionDef(self, node):
        self.bind(node.name, self.qualified+'.'+node.name)

    visit_AsyncFunctionDef = visit_FunctionDef
    visit_ClassDef = visit_FunctionDef

    def visit_Import(self, node):
        for a in node.names:
            self.bind(a.asname or a.name.split('.')[0], a.name if a.asname else a.name.split('.')[0])

    def visit_ImportFrom(self, node):
        base = import_base(node, self.package)
        for a in node.names:
            if a.name != '*':
                self.bind(a.asname or a.name, base+'.'+a.name if base else None)

    def visit_Global(self, node):
        self.globals.update(node.names)

    def visit_Nonlocal(self, node):
        self.nonlocals.update(node.names)

    def visit_ExceptHandler(self, node):
        if node.name: self.bind(node.name)
        self.generic_visit(node)

    def visit_Lambda(self, node):
        pass

    def visit_ListComp(self, node):
        pass  # Comprehension targets belong to their own scope.

    visit_SetComp = visit_ListComp
    visit_DictComp = visit_ListComp
    visit_GeneratorExp = visit_ListComp

def scope_bindings(body, qualified, package, args=None):
    b = Bindings(qualified, package)
    if args:
        for a in [*args.posonlyargs,*args.args,*args.kwonlyargs]: b.bind(a.arg)
        if args.vararg: b.bind(args.vararg.arg)
        if args.kwarg: b.bind(args.kwarg.arg)
    for n in body: b.visit(n)
    for name in b.globals | b.nonlocals: b.names.pop(name,None)
    return b

def inspect_references(trees, modules, module_name):
    def resolve(name):
        paths=modules.get(name,[])
        return paths[0] if len(paths)==1 else None

    exports={}
    for path,tree in trees.items():
        current=module_name(path)
        package=current if path.endswith('/__init__.py') else current.rpartition('.')[0]
        b=scope_bindings(tree.body,current,package)
        exports[path]={'names':set(b.names),'dynamic':any(isinstance(n,ast.ImportFrom) and any(a.name=='*' for a in n.names) for n in ast.walk(tree)) or '__getattr__' in b.names}
    calls, symbols, dynamic, unresolved = [], [], [], []

    class Visitor(ast.NodeVisitor):
        def __init__(self,path,tree):
            self.path,self.current=path,module_name(path)
            self.package=self.current if path.endswith('/__init__.py') else self.current.rpartition('.')[0]
            self.qual=self.current
            self.scopes=[(scope_bindings(tree.body,self.current,self.package),'module')]

        def lookup(self,name):
            scopes=self.scopes
            if name in scopes[-1][0].globals:scopes=scopes[:1]
            elif name in scopes[-1][0].nonlocals:scopes=scopes[:-1]
            for b,kind in reversed(scopes):
                # Python methods do not close over a class namespace.
                if kind=='class' and self.scopes[-1][1]!='class':continue
                if name in b.names:return b.names[name]
            return None

        def visit_FunctionDef(self,node):
            self.definition(node,'function')

        visit_AsyncFunctionDef=visit_FunctionDef

        def visit_ClassDef(self,node):
            self.definition(node,'class')

        def definition(self,node,kind):
            full=self.qual+'.'+node.name
            symbols.append({'path':self.path,'name':node.name,'qualified_name':full,
                            'line':node.lineno,'end_line':node.end_lineno,'kind':type(node).__name__})
            # Defaults/decorators/bases execute in the enclosing scope, not the body.
            outer=[*node.decorator_list]
            if kind=='function':outer += [*node.args.defaults,*[v for v in node.args.kw_defaults if v is not None]]
            else:outer += [*node.bases,*[k.value for k in node.keywords]]
            for item in outer:self.visit(item)
            binding=scope_bindings(node.body,full,self.package,node.args if kind=='function' else None)
            previous=self.qual;self.qual=full;self.scopes.append((binding,kind))
            for item in node.body:self.visit(item)
            self.scopes.pop();self.qual=previous

        def visit_Lambda(self,node):
            for item in [*node.args.defaults,*[v for v in node.args.kw_defaults if v is not None]]:self.visit(item)
            self.scopes.append((scope_bindings([],self.qual,self.package,node.args),'function'))
            self.visit(node.body);self.scopes.pop()

        def comprehension(self,node):
            # The first iterable is evaluated outside the comprehension scope.
            self.visit(node.generators[0].iter)
            b=Bindings(self.qual,self.package)
            for g in node.generators:b.visit(g.target)
            self.scopes.append((b,'function'))
            for i,g in enumerate(node.generators):
                if i:self.visit(g.iter)
                for condition in g.ifs:self.visit(condition)
            if isinstance(node,ast.DictComp):self.visit(node.key);self.visit(node.value)
            else:self.visit(node.elt)
            self.scopes.pop()

        visit_ListComp=comprehension
        visit_SetComp=comprehension
        visit_DictComp=comprehension
        visit_GeneratorExp=comprehension

        def visit_ImportFrom(self,node):
            base=import_base(node,self.package)
            target=resolve(base) if base else None
            if target and target in exports and not exports[target]['dynamic']:
                for a in node.names:
                    if a.name=='*' or resolve(base+'.'+a.name):continue
                    if a.name not in exports[target]['names']:
                        unresolved.append({'path':self.path,'line':node.lineno,'target':target,
                                           'symbol':base+'.'+a.name,'confidence':'review-required',
                                           'reason':'Imported name is absent from the local module binding inventory.'})

        def visit_Call(self,node):
            f=node.func;parts=[]
            while isinstance(f,ast.Attribute):parts.insert(0,f.attr);f=f.value
            if isinstance(f,ast.Name):
                parts.insert(0,f.id)
                bound=self.lookup(parts[0]);full='.'.join([bound,*parts[1:]]) if bound else None
                if (f.id in {'__import__','eval','exec','getattr'} and not bound) or full=='importlib.import_module':
                    dynamic.append({'path':self.path,'line':node.lineno,'expression':'.'.join(parts)})
                if full:
                    chunks=full.split('.')
                    for size in range(len(chunks)-1,0,-1):
                        target=resolve('.'.join(chunks[:size]))
                        if target:
                            calls.append({'source':self.path,'target':target,'line':node.lineno,
                                          'symbol':full,'caller':self.qual,'confidence':'syntactic-candidate'})
                            break
            self.generic_visit(node)

    for path,tree in trees.items():Visitor(path,tree).visit(tree)
    return calls,symbols,dynamic,unresolved
