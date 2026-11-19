# ForgeScope

**Entenda o impacto da mudança e investigue com evidências.**

[Demo interativa](https://brunnojob.github.io/forgescope/) · [Documentação completa em inglês](README.md)

Revisar um projeto desconhecido exige entender dependências, ler diffs, investigar logs e localizar manutenção pendente em várias ferramentas. O ForgeScope reúne essas etapas em um relatório local.

O núcleo Python analisa imports com AST, mostra dependências diretas e indiretas, candidatos a chamadas e testes alcançáveis pelo grafo. Os motores originais do PatchScope, CIFuse, RepoRelief e DevRescue contribuem com evidências; o catálogo RevScope orienta a escolha de ferramentas para artefatos compatíveis.

## Executar

Requisitos: Python 3.11+ e Node.js 22+ para os motores integrados. Nenhuma dependência Python externa, instalação npm, conta ou chave de API.

```sh
git clone https://github.com/brunnojob/forgescope.git
cd forgescope
python -m forgescope examples/sample --changed src/shop/pricing.py --diff examples/change.diff --log examples/build.log --output .forgescope
```

Abra `.forgescope/index.html`. O relatório inclui mapa de imports, filtros, evidências, exportação JSON e tema claro/escuro. A interface tem controles PT/EN; as evidências mantêm o texto original dos motores.

## Limites

A análise de imports indica impacto potencial, não prova quebra ou cobertura de testes. Não resolve despacho dinâmico nem chamadas entre linguagens. A revisão do diff e dos logs é heurística. A correlação usa arquivos em comum; não comprova causa raiz. O RevScope recomenda ferramentas, sem descompilar ou executar binários.

O scan não executa o código do alvo. A validação opcional de um patch executa somente o comando de testes explicitamente informado, em um worktree temporário de um repositório confiável. Isso preserva o checkout original, mas não é uma sandbox de segurança. Nenhuma correção ou PR é gerado automaticamente.

Relatórios e histórico SQLite opcional ficam locais. A redação de segredos é incompleta: revise evidências e nomes de arquivos antes de compartilhar. A demo pública usa somente dados fictícios.

Os commits e as licenças dos cinco projetos estão em [provenance.json](forgescope/vendor/provenance.json). Código MIT. Os projetos existentes permanecem independentes.


## Novidades da versão 0.2.0

- Chamadas candidatas respeitam escopos de funções, classes, parâmetros, lambdas e compreensões.
- Imports de nomes ausentes em módulos locais geram evidências para revisão, com arquivo e linha. Exports dinâmicos e módulos externos não são declarados quebrados.
- Alterações em `__init__.py` alcançam os submódulos do pacote.
- `--base HEAD~1` captura mudanças rastreadas do Git, incluindo staged e unstaged. Arquivos novos não rastreados exigem `--changed`.
- Todo relatório inclui SARIF 2.1.0. O painel também permite baixar SARIF e consultar chamadas qualificadas, referências e estados de comparação.

```sh
python -m forgescope /caminho/do/projeto --base HEAD~1 --output .forgescope
```

Esses resultados indicam onde investigar; não comprovam falha em execução nem causa raiz.
