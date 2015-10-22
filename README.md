# Workshop walkthrough

## Introduction

This workshop is designed to help developers with Relational Database backgrounds
learn about Document Database data considerations.

We'll take a very simple relational model and walk through several refactorings
as we develop a Document model.

For the workshop, we'll tackle a Task database. And to make it a bit more interesting,
it has a bunch of tables and normalization that goes beyond the basic task list:

 Table | description
 ------|------------
 Person | Registered people
 Category | Task categories
 Status | Task status
 Task | The actual task itself, with foreign keys to Person and Status
 TaskNotes | Optional notes for a task (A person may enter any number of notes)
 TaskCategories | categories for each task (a task may have multiple categories)
 TaskAssistants | Optionally, people assigned to help with tasks (but not task owner)

## Relational queries

To start, it's helpful to see the types of queries are needed to render task details.



### Getting tasks

Here, we'll get all tasks for a given person, along with description, categories
 and status.

```
select p.username, t.description, c.CategoryName, s.description from Person p
inner join task t on p.Id=t.PersonId
inner join TaskCategories tc on tc.TaskId = t.Id
inner join Category c on tc.CategoryId = c.Id
inner join Status s on t.TaskStatusId = s.Id
where p.username='David'
```

## Getting the list of people with tasks assigned

First: A list of primary task owners

```
select distinct p.username from Person p
inner join Task t on t.PersonId = p.Id
```
Now, find people who are tagged as assistants

```
select distinct p.username as 'Assistant',t.description as 'Assisting with' from Person p
inner join TaskAssistants ta on ta.PersonId = p.Id
inner join Task t on ta.TaskId = t.Id
```
# Moving to a Document database schema

## Task categories

With our relational model, we kept a normalized list of categories in a Category table:

Id | CategoryName
------
1 | Chores
2 | School
3 | Work

Since each task may have multiple categories, we have a TaskCategories table:

TaskId | CategoryId
---------
1 | 3
2 | 3
2 | 5

In a document model, we *could* keep the same model:
 - Task document, with no categories
 - A TaskCategories document, with a Task ID and array of Category id's

However: This would require two lookups. As an alternative:
 - Task document, with array of Category ID's

Since there's a bounded number of categories, we don't have an issue storing them in
the main document.

Optionally, we could denormalize data to optimize our read query:
 - Task document, with array of Category ID + Category name

This has a slight downside: If a category name changed, you might need to
find all tasks with that category, and update the task document. If you
choose to keep categories as is, and just make them *disabled* in lieu of a
newer category, then you'd never have the data update issue.

So, our new document might look like:

```
{
	Type: 'Task',
	Id: '2',
	Description: 'Finalize walkthrough doc',
	Categories: [
		{ Id: 3, CategoryName: 'Work'},
		{ Id: 5, CategoryName: 'Indoors'}
	]
}
```
}