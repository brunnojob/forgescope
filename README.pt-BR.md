# ForgeScope

**Núcleo Java 21. Painel e análise C# em .NET 10.**

[Relatório de exemplo](https://brunnojob.github.io/forgescope/) · [Documentação completa](README.md) · [Migração](docs/MIGRATION-0.3.md)

O ForgeScope reúne análise de impacto, revisão de diffs, diagnóstico de CI e manutenção em um relatório local. A versão 0.3 substitui a implementação Python/Node por Java e C#. Python e Node não são necessários para compilar ou executar o sistema.

## Componentes

- **Java:** análise com o compilador do JDK, regras de PatchScope/CIFuse/DevRescue/RepoRelief portadas, Git, relatórios HTML/JSON/Markdown/SARIF, histórico SQLite e validação de patches.
- **C#/.NET:** worker Roslyn para resolver símbolos C# e painel ASP.NET Core com filtros, seleção de arquivo, PT/EN, temas e análise local.
- **HTML/CSS:** apresentação. O relatório portátil não contém JavaScript; o painel interativo executa sua lógica no servidor C# local.

## Executar

Requisitos: JDK 21 e SDK .NET 10. Git é necessário para comparação de revisões e validação de patches.

```sh
dotnet build dotnet/ForgeScope.Worker -c Release
dotnet build dotnet/ForgeScope.Workbench -c Release
java tools/Dependencies.java
java tools/Build.java test
java -jar build/forgescope.jar examples/sample --changed java/src/shop/Pricing.java --changed csharp/Pricing.cs --diff examples/change.diff --log examples/build.log --csharp-worker dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll --output .forgescope
```

O passo Dependencies é opcional para análise, mas necessário para `--history`. Baixa SQLite JDBC e SLF4J com hashes fixos. O scan não faz downloads.

Abra `.forgescope/index.html` para o relatório portátil. Para o painel completo:

```sh
dotnet dotnet/ForgeScope.Workbench/bin/Release/net10.0/ForgeScope.Workbench.dll --report .forgescope/report.json --repo examples/sample --jar build/forgescope.jar --worker dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll
```

Acesse **http://127.0.0.1:8765**. O painel só escuta no endereço local, usa nonce para novas análises e não executa comandos vindos de logs. Sem `--repo` e `--jar`, funciona como visualizador somente leitura.

## Limites importantes

A análise Java/C# usa os compiladores em um ambiente isolado: não executa o código alvo, annotation processors, MSBuild, analyzers do projeto ou geradores. Referências externas ausentes aparecem como diagnósticos. Evidências e caminhos não comprovam causa raiz nem cobertura de testes.

A compatibilidade com arquivos Python passou a ser leitura léxica de imports; a análise Python AST da versão anterior permanece no histórico Git. Os motores de revisão são portas Java e podem produzir textos/identidades diferentes dos anteriores. O histórico SQLite mantém o esquema existente.

GitHub Pages mostra um relatório estático fictício. O painel .NET e as análises reais rodam em localhost. O catálogo RevScope indica ferramentas, sem executar engenharia reversa.

Código MIT, com créditos e licenças originais preservados.

## Idiomas e demonstração guiada

O relatório no [Pages](https://brunnojob.github.io/forgescope/pt-br.html) está disponível integralmente em **PT-BR, inglês e espanhol**. Os links de idioma trocam títulos, resultados gerados, ações, estados, limitações e o tour. Caminhos, nomes de métodos e evidências originais de logs/compiladores são preservados.

Clique em **Iniciar demonstração** para percorrer seis etapas, com próximo, anterior, fechar e concluir. O tour funciona no Pages e nos relatórios offline, sem JavaScript. No painel C#, selecione o idioma e aplique os filtros; novas análises preservam idioma e tema.
